using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;

public class MeshMakerCore
{
    private MeshMakerEditor w;

    public MeshMakerCore(MeshMakerEditor window)
    {
        w = window;
    }

    public void UpdateMeshChanges()
    {
        w.workingMesh.vertices = w.customVertices;
        w.workingMesh.uv = w.customUVs;
        w.workingMesh.RecalculateNormals();
        w.workingMesh.RecalculateBounds();
        ForceUpdateMeshFilter();
        w.hasUnsavedMeshChanges = true;
        SceneView.RepaintAll();
    }

    public void ForceUpdateMeshFilter()
    {
        if (w.currentObject != null)
        {
            var filter = w.currentObject.GetComponent<MeshFilter>();
            if (filter != null) filter.sharedMesh = w.workingMesh;
        }
    }

    public void ResetUVProjectionBounds()
    {
        w.workingMesh.RecalculateBounds();
        Bounds b = w.workingMesh.bounds;
        w.cachedUVProjectionSize = Mathf.Max(b.size.x, b.size.y);
        w.cachedUVProjectionSize = Mathf.Max(w.cachedUVProjectionSize, 0.001f);
        w.cachedUVProjectionCenter = b.center;
    }

    public void ProjectUVs()
    {
        if (w.workingMesh == null || w.customVertices == null || w.customUVs == null) return;

        if (w.cachedUVProjectionSize <= 0.001f) ResetUVProjectionBounds();

        Undo.RecordObject(w, "Project UVs");
        Undo.RecordObject(w.workingMesh, "Project UVs");

        for (int i = 0; i < w.customVertices.Length; i++)
        {
            Vector3 localPos = w.customVertices[i];
            float normX = (localPos.x - w.cachedUVProjectionCenter.x) / w.cachedUVProjectionSize;
            float normY = (localPos.y - w.cachedUVProjectionCenter.y) / w.cachedUVProjectionSize;
            float u = normX * w.uvTiling + 0.5f;
            float v = normY * w.uvTiling + 0.5f;
            w.customUVs[i] = new Vector2(u, v);
        }

        UpdateMeshChanges();
    }

    public void UpdateUVPreview()
    {
        if (!w.editUVMode || w.currentObject == null) { CleanupUVPreview(); return; }

        var targetRenderer = w.currentObject.GetComponent<MeshRenderer>();
        if (targetRenderer != null)
        {
            if (w.originalMaterial == null) w.originalMaterial = targetRenderer.sharedMaterial;

            if (targetRenderer.sharedMaterial == w.originalMaterial)
            {
                Material tempMat = w.originalMaterial != null ? new Material(w.originalMaterial) : new Material(Shader.Find("Standard"));
                tempMat.name = "TempUVMaterial";
                tempMat.hideFlags = HideFlags.DontSave;
                targetRenderer.sharedMaterial = tempMat;
            }

            if (targetRenderer.sharedMaterial != null) targetRenderer.sharedMaterial.mainTexture = w.tempTexture;
        }

        if (w.uvPreviewObj == null)
        {
            w.uvPreviewObj = GameObject.CreatePrimitive(PrimitiveType.Quad);
            w.uvPreviewObj.name = "[UV Preview]";
            w.uvPreviewObj.hideFlags = HideFlags.DontSave;
            GameObject.DestroyImmediate(w.uvPreviewObj.GetComponent<Collider>());
        }

        if (w.cachedUVProjectionSize <= 0.001f) ResetUVProjectionBounds();

        float tileSize = w.cachedUVProjectionSize / w.uvTiling;

        w.uvPreviewObj.transform.SetParent(w.currentObject.transform);
        w.uvPreviewObj.transform.localPosition = w.cachedUVProjectionCenter + new Vector3(0, 0, -0.05f);
        w.uvPreviewObj.transform.localScale = new Vector3(tileSize, tileSize, 1);

        var r = w.uvPreviewObj.GetComponent<MeshRenderer>();
        if (r.sharedMaterial == null || r.sharedMaterial.name != "PreviewUVMat")
        {
            r.sharedMaterial = new Material(Shader.Find("Unlit/Transparent") ?? Shader.Find("Standard"));
            r.sharedMaterial.name = "PreviewUVMat";
        }

        r.sharedMaterial.mainTexture = w.tempTexture;
        r.sharedMaterial.color = new Color(1, 1, 1, 0.4f);
    }

    public void CleanupUVPreview()
    {
        if (w.uvPreviewObj != null) GameObject.DestroyImmediate(w.uvPreviewObj);

        if (w.currentObject != null && w.originalMaterial != null)
        {
            var r = w.currentObject.GetComponent<MeshRenderer>();
            if (r != null && r.sharedMaterial != w.originalMaterial)
            {
                if (r.sharedMaterial != null && r.sharedMaterial.name == "TempUVMaterial")
                    GameObject.DestroyImmediate(r.sharedMaterial);
                r.sharedMaterial = w.originalMaterial;
            }
        }
        w.originalMaterial = null;
    }

    // --- 수정됨: 무조건 UV 크기만큼 강제 확장 후 알파에 맞춰 당기는 로직 ---
    public void AutoAdjustVerticesToAlpha()
    {
        if (w.tempTexture == null)
        {
            EditorUtility.DisplayDialog("텍스처 확인", "UV 에디터에서 Temp Texture를 먼저 지정해야 외곽선을 계산할 수 있습니다.", "확인");
            return;
        }

        if (w.cachedUVProjectionSize <= 0.001f) ResetUVProjectionBounds();

        RenderTexture tmp = RenderTexture.GetTemporary(w.tempTexture.width, w.tempTexture.height, 0, RenderTextureFormat.Default, RenderTextureReadWrite.Linear);
        Graphics.Blit(w.tempTexture, tmp);
        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = tmp;
        Texture2D tex = new Texture2D(w.tempTexture.width, w.tempTexture.height, TextureFormat.RGBA32, false);
        tex.ReadPixels(new Rect(0, 0, tmp.width, tmp.height), 0, 0);
        tex.Apply();
        RenderTexture.active = previous;
        RenderTexture.ReleaseTemporary(tmp);

        Color[] pixels = tex.GetPixels();
        int width = tex.width;
        int height = tex.height;

        List<Vector2> boundaryUVs = new List<Vector2>();
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (pixels[y * width + x].a >= 0.1f)
                {
                    bool isEdge = false;
                    if (x == 0 || x == width - 1 || y == 0 || y == height - 1) isEdge = true;
                    else if (pixels[y * width + (x - 1)].a < 0.1f ||
                             pixels[y * width + (x + 1)].a < 0.1f ||
                             pixels[(y - 1) * width + x].a < 0.1f ||
                             pixels[(y + 1) * width + x].a < 0.1f)
                    {
                        isEdge = true;
                    }

                    if (isEdge) boundaryUVs.Add(new Vector2((float)x / width, (float)y / height));
                }
            }
        }

        if (boundaryUVs.Count == 0)
        {
            Object.DestroyImmediate(tex);
            EditorUtility.DisplayDialog("알파 확인", "텍스처에서 알파(투명) 영역이나 외곽선을 찾을 수 없습니다.", "확인");
            return;
        }

        Undo.RecordObject(w, "Auto Adjust to Alpha");
        Undo.RecordObject(w.workingMesh, "Auto Adjust to Alpha");

        // 1. [핵심 변경 사항] 무조건 현재 버텍스들을 텍스처(UV) 영역 끝까지 강제로 팽창시킵니다.
        Bounds vBounds = new Bounds(w.customVertices[0], Vector3.zero);
        for (int i = 1; i < w.customVertices.Length; i++) vBounds.Encapsulate(w.customVertices[i]);

        float actualSize = w.cachedUVProjectionSize / w.uvTiling;

        for (int i = 0; i < w.customVertices.Length; i++)
        {
            Vector3 local = w.customVertices[i] - vBounds.center;
            float nx = vBounds.size.x > 0.001f ? local.x / vBounds.size.x : 0f;
            float ny = vBounds.size.y > 0.001f ? local.y / vBounds.size.y : 0f;

            w.customVertices[i] = new Vector3(
                w.cachedUVProjectionCenter.x + nx * actualSize,
                w.cachedUVProjectionCenter.y + ny * actualSize,
                w.customVertices[i].z
            );

            // 위치 변경에 맞게 내부 UV 좌표도 갱신
            float normX = (w.customVertices[i].x - w.cachedUVProjectionCenter.x) / w.cachedUVProjectionSize;
            float normY = (w.customVertices[i].y - w.cachedUVProjectionCenter.y) / w.cachedUVProjectionSize;
            w.customUVs[i] = new Vector2(normX * w.uvTiling + 0.5f, normY * w.uvTiling + 0.5f);
        }

        // 2. 외곽선(알파) 스냅 (수축)
        bool[] isFixed = new bool[w.customVertices.Length];
        Vector3[] newPositions = new Vector3[w.customVertices.Length];

        for (int i = 0; i < w.customVertices.Length; i++)
        {
            Vector3 vPos = w.customVertices[i];
            float u = w.customUVs[i].x;
            float v = w.customUVs[i].y;

            int px = Mathf.Clamp(Mathf.FloorToInt(u * width), 0, width - 1);
            int py = Mathf.Clamp(Mathf.FloorToInt(v * height), 0, height - 1);

            float alpha = 0f;
            if (u >= 0f && u <= 1f && v >= 0f && v <= 1f) alpha = pixels[py * width + px].a;

            if (alpha < 0.1f)
            {
                Vector2 currentUV = new Vector2(u, v);
                Vector2 nearestUV = boundaryUVs[0];
                float minDist = Vector2.Distance(currentUV, nearestUV);

                for (int j = 1; j < boundaryUVs.Count; j++)
                {
                    float d = Vector2.Distance(currentUV, boundaryUVs[j]);
                    if (d < minDist)
                    {
                        minDist = d;
                        nearestUV = boundaryUVs[j];
                    }
                }

                float targetX = (nearestUV.x - 0.5f) / w.uvTiling * w.cachedUVProjectionSize + w.cachedUVProjectionCenter.x;
                float targetY = (nearestUV.y - 0.5f) / w.uvTiling * w.cachedUVProjectionSize + w.cachedUVProjectionCenter.y;
                Vector3 targetPos = new Vector3(targetX, targetY, vPos.z);

                Vector2 uvDir = currentUV - nearestUV;
                Vector3 worldDir = Vector3.zero;
                if (uvDir.sqrMagnitude > 0.00001f) worldDir = new Vector3(uvDir.x, uvDir.y, 0).normalized;
                else worldDir = (targetPos - w.cachedUVProjectionCenter).normalized;

                worldDir.z = 0;
                targetPos += worldDir * w.alphaPadding;

                newPositions[i] = targetPos;
                isFixed[i] = true; // 알파 바깥에서 끌려온 점은 외곽 형태를 잡는 고정 핀
            }
            else
            {
                newPositions[i] = vPos;
                isFixed[i] = false; // 알파 안에 들어와 있는 점은 부드럽게 재배치될 수 있도록 풂
            }
        }

        // 3. 내부 버텍스 밀도 균일화 (Laplacian Smoothing)
        List<int>[] adj = new List<int>[w.customVertices.Length];
        for (int i = 0; i < w.customVertices.Length; i++) adj[i] = new List<int>();

        int[] tris = w.workingMesh.triangles;
        for (int i = 0; i < tris.Length; i += 3)
        {
            int v1 = tris[i], v2 = tris[i + 1], v3 = tris[i + 2];
            if (!adj[v1].Contains(v2)) adj[v1].Add(v2);
            if (!adj[v1].Contains(v3)) adj[v1].Add(v3);
            if (!adj[v2].Contains(v1)) adj[v2].Add(v1);
            if (!adj[v2].Contains(v3)) adj[v2].Add(v3);
            if (!adj[v3].Contains(v1)) adj[v3].Add(v1);
            if (!adj[v3].Contains(v2)) adj[v3].Add(v2);
        }

        int iterations = 50;
        for (int iter = 0; iter < iterations; iter++)
        {
            Vector3[] tempPos = new Vector3[w.customVertices.Length];
            for (int i = 0; i < w.customVertices.Length; i++)
            {
                if (isFixed[i])
                {
                    tempPos[i] = newPositions[i];
                }
                else
                {
                    if (adj[i].Count > 0)
                    {
                        Vector3 avg = Vector3.zero;
                        for (int j = 0; j < adj[i].Count; j++)
                        {
                            avg += newPositions[adj[i][j]];
                        }
                        tempPos[i] = avg / adj[i].Count;
                    }
                    else
                    {
                        tempPos[i] = newPositions[i];
                    }
                }
            }
            newPositions = tempPos;
        }

        for (int i = 0; i < w.customVertices.Length; i++)
        {
            w.customVertices[i] = newPositions[i];
        }

        Object.DestroyImmediate(tex);

        if (w.editUVMode) ProjectUVs();
        else UpdateMeshChanges();
    }

    // ------------------------------------------------------------

    public void ConnectSelectedVertices()
    {
        if (w.selectedIndices.Count != 2) return;
        int idxA = w.selectedIndices[0];
        int idxB = w.selectedIndices[1];

        Undo.RecordObject(w, "Connect Vertices");
        Undo.RecordObject(w.workingMesh, "Connect Vertices");

        Vector2 posA = new Vector2(w.customVertices[idxA].x, w.customVertices[idxA].y);
        Vector2 posB = new Vector2(w.customVertices[idxB].x, w.customVertices[idxB].y);

        List<int> tris = new List<int>(w.workingMesh.triangles);
        int maxIterations = 200;
        int iterations = 0;
        bool flippedAny = true;

        while (flippedAny && iterations < maxIterations)
        {
            flippedAny = false;
            iterations++;

            for (int i = 0; i < tris.Count; i += 3)
            {
                for (int j = i + 3; j < tris.Count; j += 3)
                {
                    List<int> sharedVerts = new List<int>();
                    List<int> unsharedVerts = new List<int>();

                    for (int x = 0; x < 3; x++)
                    {
                        int vi = tris[i + x];
                        bool shared = false;
                        for (int y = 0; y < 3; y++) { if (vi == tris[j + y]) { shared = true; break; } }
                        if (shared) sharedVerts.Add(vi); else unsharedVerts.Add(vi);
                    }

                    for (int y = 0; y < 3; y++)
                    {
                        int vj = tris[j + y];
                        bool shared = false;
                        for (int x = 0; x < 3; x++) { if (vj == tris[i + x]) { shared = true; break; } }
                        if (!shared) unsharedVerts.Add(vj);
                    }

                    if (sharedVerts.Count == 2 && unsharedVerts.Count == 2)
                    {
                        Vector2 s1 = new Vector2(w.customVertices[sharedVerts[0]].x, w.customVertices[sharedVerts[0]].y);
                        Vector2 s2 = new Vector2(w.customVertices[sharedVerts[1]].x, w.customVertices[sharedVerts[1]].y);
                        Vector2 u1 = new Vector2(w.customVertices[unsharedVerts[0]].x, w.customVertices[unsharedVerts[0]].y);
                        Vector2 u2 = new Vector2(w.customVertices[unsharedVerts[1]].x, w.customVertices[unsharedVerts[1]].y);

                        if (LineIntersectsLine(posA, posB, s1, s2))
                        {
                            if (LineIntersectsLine(s1, s2, u1, u2))
                            {
                                int vA = unsharedVerts[0];
                                int vB = unsharedVerts[1];
                                int vC = sharedVerts[0];
                                int vD = sharedVerts[1];

                                Vector3 oldCross = Vector3.Cross(w.customVertices[tris[i + 1]] - w.customVertices[tris[i]], w.customVertices[tris[i + 2]] - w.customVertices[tris[i]]);
                                float oldSign = oldCross.z == 0 ? 1 : Mathf.Sign(oldCross.z);

                                Vector3 newCross1 = Vector3.Cross(w.customVertices[vB] - w.customVertices[vA], w.customVertices[vC] - w.customVertices[vA]);
                                if (Mathf.Sign(newCross1.z) != oldSign && newCross1.z != 0)
                                {
                                    tris[i] = vB; tris[i + 1] = vA; tris[i + 2] = vC;
                                }
                                else
                                {
                                    tris[i] = vA; tris[i + 1] = vB; tris[i + 2] = vC;
                                }

                                Vector3 newCross2 = Vector3.Cross(w.customVertices[vB] - w.customVertices[vA], w.customVertices[vD] - w.customVertices[vA]);
                                if (Mathf.Sign(newCross2.z) != oldSign && newCross2.z != 0)
                                {
                                    tris[j] = vB; tris[j + 1] = vA; tris[j + 2] = vD;
                                }
                                else
                                {
                                    tris[j] = vA; tris[j + 1] = vB; tris[j + 2] = vD;
                                }

                                flippedAny = true;
                                break;
                            }
                        }
                    }
                }
                if (flippedAny) break;
            }
        }

        w.workingMesh.triangles = tris.ToArray();
        w.selectedIndices.Clear();

        if (w.editUVMode) ProjectUVs();
        else UpdateMeshChanges();
    }

    private bool LineIntersectsLine(Vector2 a1, Vector2 a2, Vector2 b1, Vector2 b2)
    {
        float denominator = (b2.y - b1.y) * (a2.x - a1.x) - (b2.x - b1.x) * (a2.y - a1.y);
        if (Mathf.Abs(denominator) < 1e-5f) return false;

        float u_a = ((b2.x - b1.x) * (a1.y - b1.y) - (b2.y - b1.y) * (a1.x - b1.x)) / denominator;
        float u_b = ((a2.x - a1.x) * (a1.y - b1.y) - (a2.y - a1.y) * (a1.x - b1.x)) / denominator;

        return (u_a > 0.001f && u_a < 0.999f && u_b > 0.001f && u_b < 0.999f);
    }

    public void InsertVertexOnEdge(int a, int b, Vector3 localPos)
    {
        Undo.RecordObject(w, "Insert Vertex");
        Undo.RecordObject(w.workingMesh, "Insert Vertex");

        List<Vector3> newVerts = new List<Vector3>(w.customVertices);
        newVerts.Add(localPos);
        int newIdx = newVerts.Count - 1;

        List<Vector2> newUVs = new List<Vector2>(w.customUVs);
        float t = Vector3.Distance(w.customVertices[a], localPos) / Vector3.Distance(w.customVertices[a], w.customVertices[b]);
        newUVs.Add(Vector2.Lerp(w.customUVs[a], w.customUVs[b], t));

        int[] oldTris = w.workingMesh.triangles;
        List<int> newTris = new List<int>();
        for (int i = 0; i < oldTris.Length; i += 3)
        {
            int v1 = oldTris[i], v2 = oldTris[i + 1], v3 = oldTris[i + 2];
            bool split = false;
            if ((v1 == a && v2 == b) || (v1 == b && v2 == a)) { newTris.AddRange(new int[] { v1, newIdx, v3, newIdx, v2, v3 }); split = true; }
            else if ((v2 == a && v3 == b) || (v2 == b && v3 == a)) { newTris.AddRange(new int[] { v2, newIdx, v1, newIdx, v3, v1 }); split = true; }
            else if ((v3 == a && v1 == b) || (v3 == b && v1 == a)) { newTris.AddRange(new int[] { v3, newIdx, v2, newIdx, v1, v2 }); split = true; }
            if (!split) newTris.AddRange(new int[] { v1, v2, v3 });
        }

        w.customVertices = newVerts.ToArray();
        w.customUVs = newUVs.ToArray();

        w.workingMesh.Clear();
        w.workingMesh.vertices = w.customVertices;
        w.workingMesh.triangles = newTris.ToArray();
        w.workingMesh.uv = w.customUVs;

        if (w.editUVMode) ProjectUVs();
        else UpdateMeshChanges();
    }

    public void RemoveSelectedVertices()
    {
        if (w.customVertices.Length <= 3) return;
        Undo.RecordObject(w, "Remove Vertices");
        Undo.RecordObject(w.workingMesh, "Remove Vertices");

        List<Vector3> newVerts = new List<Vector3>();
        List<Vector2> newUVs = new List<Vector2>();
        Dictionary<int, int> oldToNewIdx = new Dictionary<int, int>();

        for (int i = 0; i < w.customVertices.Length; i++)
        {
            if (!w.selectedIndices.Contains(i))
            {
                oldToNewIdx[i] = newVerts.Count;
                newVerts.Add(w.customVertices[i]);
                newUVs.Add(w.customUVs[i]);
            }
        }

        int[] oldTris = w.workingMesh.triangles;
        List<int> newTris = new List<int>();
        for (int i = 0; i < oldTris.Length; i += 3)
        {
            int v1 = oldTris[i], v2 = oldTris[i + 1], v3 = oldTris[i + 2];
            if (!w.selectedIndices.Contains(v1) && !w.selectedIndices.Contains(v2) && !w.selectedIndices.Contains(v3))
            {
                newTris.Add(oldToNewIdx[v1]); newTris.Add(oldToNewIdx[v2]); newTris.Add(oldToNewIdx[v3]);
            }
        }

        w.customVertices = newVerts.ToArray();
        w.customUVs = newUVs.ToArray();
        w.selectedIndices.Clear();

        w.workingMesh.Clear();
        w.workingMesh.vertices = w.customVertices;
        w.workingMesh.triangles = newTris.ToArray();
        w.workingMesh.uv = w.customUVs;

        if (w.editUVMode) ProjectUVs();
        else UpdateMeshChanges();
    }

    public void MergeSelectedVertices()
    {
        if (w.selectedIndices.Count < 2) return;

        Undo.RecordObject(w, "Merge Vertices");
        Undo.RecordObject(w.workingMesh, "Merge Vertices");

        Vector3 centerPos = Vector3.zero;
        Vector2 centerUV = Vector2.zero;
        foreach (int idx in w.selectedIndices)
        {
            centerPos += w.customVertices[idx];
            centerUV += w.customUVs[idx];
        }
        centerPos /= w.selectedIndices.Count;
        centerUV /= w.selectedIndices.Count;

        int keepIdx = w.selectedIndices[0];
        w.customVertices[keepIdx] = centerPos;
        w.customUVs[keepIdx] = centerUV;

        HashSet<int> removeSet = new HashSet<int>(w.selectedIndices);
        removeSet.Remove(keepIdx);

        int[] oldTris = w.workingMesh.triangles;
        List<int> newTris = new List<int>();

        for (int i = 0; i < oldTris.Length; i += 3)
        {
            int v1 = oldTris[i];
            int v2 = oldTris[i + 1];
            int v3 = oldTris[i + 2];

            if (removeSet.Contains(v1)) v1 = keepIdx;
            if (removeSet.Contains(v2)) v2 = keepIdx;
            if (removeSet.Contains(v3)) v3 = keepIdx;

            if (v1 == v2 || v2 == v3 || v3 == v1) continue;

            newTris.Add(v1);
            newTris.Add(v2);
            newTris.Add(v3);
        }

        w.workingMesh.triangles = newTris.ToArray();
        CleanupOrphanedVertices(newTris);

        w.selectedIndices.Clear();
        if (w.editUVMode) ProjectUVs();
        else UpdateMeshChanges();
    }

    public void AutoMergeVertices()
    {
        if (w.customVertices == null || w.customVertices.Length == 0) return;

        Undo.RecordObject(w, "Auto Merge Vertices");
        Undo.RecordObject(w.workingMesh, "Auto Merge Vertices");

        Vector3[] verts = w.customVertices;
        int[] oldTris = w.workingMesh.triangles;

        List<Vector3> newVerts = new List<Vector3>();
        List<Vector2> newUVs = new List<Vector2>();
        int[] oldToNewIdx = new int[verts.Length];

        for (int i = 0; i < verts.Length; i++)
        {
            int foundIdx = -1;
            for (int j = 0; j < newVerts.Count; j++)
            {
                if (Vector3.Distance(verts[i], newVerts[j]) <= w.autoMergeDistance)
                {
                    foundIdx = j;
                    break;
                }
            }

            if (foundIdx != -1)
            {
                oldToNewIdx[i] = foundIdx;
            }
            else
            {
                oldToNewIdx[i] = newVerts.Count;
                newVerts.Add(verts[i]);
                newUVs.Add(w.customUVs[i]);
            }
        }

        List<int> newTris = new List<int>();
        for (int i = 0; i < oldTris.Length; i += 3)
        {
            int v1 = oldToNewIdx[oldTris[i]];
            int v2 = oldToNewIdx[oldTris[i + 1]];
            int v3 = oldToNewIdx[oldTris[i + 2]];

            if (v1 == v2 || v2 == v3 || v3 == v1) continue;

            newTris.Add(v1);
            newTris.Add(v2);
            newTris.Add(v3);
        }

        w.customVertices = newVerts.ToArray();
        w.customUVs = newUVs.ToArray();
        w.workingMesh.Clear();
        w.workingMesh.vertices = w.customVertices;
        w.workingMesh.triangles = newTris.ToArray();
        w.workingMesh.uv = w.customUVs;

        CleanupOrphanedVertices(newTris);

        w.selectedIndices.Clear();
        if (w.editUVMode) ProjectUVs();
        else UpdateMeshChanges();
    }

    public void DissolveSelectedVertices()
    {
        if (w.customVertices.Length <= 3 || w.selectedIndices.Count == 0) return;
        Undo.RecordObject(w, "Dissolve Vertices");
        Undo.RecordObject(w.workingMesh, "Dissolve Vertices");

        List<int> currentTris = new List<int>(w.workingMesh.triangles);
        bool changed = false;

        foreach (int vIndex in w.selectedIndices)
        {
            if (DissolveSingleVertex(vIndex, currentTris)) changed = true;
        }

        if (changed)
        {
            CleanupOrphanedVertices(currentTris);
            w.selectedIndices.Clear();
            if (w.editUVMode) ProjectUVs();
            else UpdateMeshChanges();
        }
    }

    private bool DissolveSingleVertex(int vIndex, List<int> currentTris)
    {
        List<int> relatedTris = new List<int>();
        List<int> otherTris = new List<int>();

        for (int i = 0; i < currentTris.Count; i += 3)
        {
            int v1 = currentTris[i];
            int v2 = currentTris[i + 1];
            int v3 = currentTris[i + 2];

            if (v1 == vIndex || v2 == vIndex || v3 == vIndex)
            {
                relatedTris.Add(v1); relatedTris.Add(v2); relatedTris.Add(v3);
            }
            else
            {
                otherTris.Add(v1); otherTris.Add(v2); otherTris.Add(v3);
            }
        }

        if (relatedTris.Count == 0) return false;

        List<Vector2Int> boundaryEdges = new List<Vector2Int>();
        for (int i = 0; i < relatedTris.Count; i += 3)
        {
            int v1 = relatedTris[i];
            int v2 = relatedTris[i + 1];
            int v3 = relatedTris[i + 2];

            if (v1 == vIndex) boundaryEdges.Add(new Vector2Int(v2, v3));
            else if (v2 == vIndex) boundaryEdges.Add(new Vector2Int(v3, v1));
            else if (v3 == vIndex) boundaryEdges.Add(new Vector2Int(v1, v2));
        }

        if (boundaryEdges.Count < 2) return false;

        int startIndex = 0;
        for (int i = 0; i < boundaryEdges.Count; i++)
        {
            bool isTargetOfAnother = false;
            for (int j = 0; j < boundaryEdges.Count; j++)
            {
                if (i != j && boundaryEdges[j].y == boundaryEdges[i].x)
                {
                    isTargetOfAnother = true;
                    break;
                }
            }
            if (!isTargetOfAnother) { startIndex = i; break; }
        }

        List<int> polygon = new List<int>();
        Vector2Int currentEdge = boundaryEdges[startIndex];
        polygon.Add(currentEdge.x);
        polygon.Add(currentEdge.y);
        boundaryEdges.RemoveAt(startIndex);

        while (boundaryEdges.Count > 0)
        {
            int lastVertex = polygon[polygon.Count - 1];
            bool found = false;
            for (int i = 0; i < boundaryEdges.Count; i++)
            {
                if (boundaryEdges[i].x == lastVertex)
                {
                    polygon.Add(boundaryEdges[i].y);
                    boundaryEdges.RemoveAt(i);
                    found = true;
                    break;
                }
            }
            if (!found) break;
        }

        if (polygon.Count > 1 && polygon[0] == polygon[polygon.Count - 1])
        {
            polygon.RemoveAt(polygon.Count - 1);
        }

        if (polygon.Count < 3) return false;

        List<int> newTris = new List<int>();
        int vStart = polygon[0];
        for (int i = 1; i < polygon.Count - 1; i++)
        {
            newTris.Add(vStart);
            newTris.Add(polygon[i]);
            newTris.Add(polygon[i + 1]);
        }

        currentTris.Clear();
        currentTris.AddRange(otherTris);
        currentTris.AddRange(newTris);

        return true;
    }

    public void CleanupOrphanedVertices(List<int> currentTris)
    {
        HashSet<int> usedIndices = new HashSet<int>(currentTris);

        List<Vector3> newVerts = new List<Vector3>();
        List<Vector2> newUVs = new List<Vector2>();
        Dictionary<int, int> oldToNewIdx = new Dictionary<int, int>();

        for (int i = 0; i < w.customVertices.Length; i++)
        {
            if (usedIndices.Contains(i))
            {
                oldToNewIdx[i] = newVerts.Count;
                newVerts.Add(w.customVertices[i]);
                newUVs.Add(w.customUVs[i]);
            }
        }

        for (int i = 0; i < currentTris.Count; i++)
        {
            currentTris[i] = oldToNewIdx[currentTris[i]];
        }

        w.customVertices = newVerts.ToArray();
        w.customUVs = newUVs.ToArray();

        w.workingMesh.Clear();
        w.workingMesh.vertices = w.customVertices;
        w.workingMesh.triangles = currentTris.ToArray();
        w.workingMesh.uv = w.customUVs;
    }

    public void OverwriteExisting()
    {
        if (w.originalMeshAsset == null || !AssetDatabase.Contains(w.originalMeshAsset)) { SaveAsNew(); return; }

        Undo.RecordObject(w.originalMeshAsset, "Overwrite Mesh Asset");
        w.originalMeshAsset.Clear();
        w.originalMeshAsset.vertices = w.workingMesh.vertices;
        w.originalMeshAsset.triangles = w.workingMesh.triangles;
        w.originalMeshAsset.uv = w.workingMesh.uv;
        w.originalMeshAsset.normals = w.workingMesh.normals;
        w.originalMeshAsset.tangents = w.workingMesh.tangents;
        w.originalMeshAsset.RecalculateNormals();
        w.originalMeshAsset.RecalculateBounds();
        EditorUtility.SetDirty(w.originalMeshAsset);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        CompleteSaving(w.originalMeshAsset, w.originalMeshAsset.name);
    }

    public void SaveAsNew()
    {
        Mesh meshToSave = Object.Instantiate(w.workingMesh);
        meshToSave.name = string.IsNullOrEmpty(w.meshName) ? "NewMesh" : w.meshName;
        string path = w.saveFolder ? AssetDatabase.GetAssetPath(w.saveFolder) : "Assets";
        string fullPath = AssetDatabase.GenerateUniqueAssetPath($"{path}/{meshToSave.name}.asset");
        AssetDatabase.CreateAsset(meshToSave, fullPath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        CompleteSaving(meshToSave, Path.GetFileNameWithoutExtension(fullPath));
    }

    public void CompleteSaving(Mesh savedAsset, string newName)
    {
        CleanupUVPreview();
        if (w.currentObject != null)
        {
            var filter = w.currentObject.GetComponent<MeshFilter>();
            filter.sharedMesh = savedAsset;
            w.currentObject.name = newName.Replace("[Preview]", "").Trim();
            Tools.hidden = false;
        }
        w.originalMeshAsset = savedAsset;
        w.workingMesh = Object.Instantiate(savedAsset);
        w.workingMesh.MarkDynamic();
        w.customVertices = w.workingMesh.vertices;
        w.customUVs = w.workingMesh.uv;
        w.editMode = false; w.editUVMode = false; w.isPreviewInstance = false; w.hasUnsavedMeshChanges = false; w.selectedIndices.Clear();
        SceneView.RepaintAll();
    }

    public void StartEditing(GameObject obj)
    {
        if (obj == null) return;
        w.currentObject = obj;
        var filter = obj.GetComponent<MeshFilter>();
        if (filter == null) return;

        w.originalMeshAsset = filter.sharedMesh;
        w.workingMesh = w.originalMeshAsset != null ? Object.Instantiate(w.originalMeshAsset) : new Mesh();
        w.workingMesh.MarkDynamic();
        w.workingMesh.name = w.originalMeshAsset != null ? w.originalMeshAsset.name : w.meshName;

        filter.sharedMesh = w.workingMesh;
        w.customVertices = w.workingMesh.vertices;

        if (w.workingMesh.uv != null && w.workingMesh.uv.Length == w.customVertices.Length)
            w.customUVs = w.workingMesh.uv;
        else
            w.customUVs = new Vector2[w.customVertices.Length];

        w.cachedUVProjectionSize = 0f;
        w.originalMaterial = null;

        w.isPreviewInstance = obj.name.Contains("[Preview]");
        if (w.isPreviewInstance) w.hasUnsavedMeshChanges = true;

        w.editMode = w.isPreviewInstance;
        if (w.editMode) Tools.hidden = true;

        w.selectedIndices.Clear();
    }

    public void CancelVertexChanges()
    {
        if (w.originalMeshAsset != null)
        {
            w.workingMesh = Object.Instantiate(w.originalMeshAsset);
            w.workingMesh.MarkDynamic();
            w.customVertices = w.workingMesh.vertices;
            w.customUVs = w.workingMesh.uv;
            w.currentObject.GetComponent<MeshFilter>().sharedMesh = w.workingMesh;
        }
    }

    public void SpawnNewMesh()
    {
        if (w.currentObject && w.isPreviewInstance) GameObject.DestroyImmediate(w.currentObject);
        w.currentObject = new GameObject("[Preview] " + w.meshName);
        w.currentObject.transform.position = SceneView.lastActiveSceneView ? SceneView.lastActiveSceneView.pivot : Vector3.zero;

        w.currentObject.AddComponent<MeshFilter>();
        var renderer = w.currentObject.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));

        w.isPreviewInstance = true;
        w.workingMesh = new Mesh();
        w.workingMesh.MarkDynamic();
        w.currentObject.GetComponent<MeshFilter>().sharedMesh = w.workingMesh;
        w.cachedUVProjectionSize = 0f;
        ResetPreviewVertices();
        Selection.activeGameObject = w.currentObject;

        StartEditing(w.currentObject);
        w.hasUnsavedMeshChanges = true;
    }

    public void ResetPreviewVertices()
    {
        w.customVertices = GenerateGridVertices(w.segmentsX, w.segmentsY, w.width, w.height);
        w.customUVs = GenerateGridUVs(w.segmentsX, w.segmentsY);

        w.workingMesh.Clear();
        w.workingMesh.vertices = w.customVertices;
        w.workingMesh.uv = w.customUVs;

        int[] tris = new int[w.segmentsX * w.segmentsY * 6];
        int idx = 0;
        for (int y = 0; y < w.segmentsY; y++)
        {
            for (int x = 0; x < w.segmentsX; x++)
            {
                int start = y * (w.segmentsX + 1) + x;
                tris[idx++] = start; tris[idx++] = start + w.segmentsX + 1; tris[idx++] = start + 1;
                tris[idx++] = start + 1; tris[idx++] = start + w.segmentsX + 1; tris[idx++] = start + w.segmentsX + 2;
            }
        }
        w.workingMesh.triangles = tris;
        w.workingMesh.RecalculateNormals();
        w.workingMesh.RecalculateBounds();
        ForceUpdateMeshFilter();
    }

    private Vector3[] GenerateGridVertices(int sx, int sy, float width, float height)
    {
        Vector3[] v = new Vector3[(sx + 1) * (sy + 1)];
        for (int y = 0; y <= sy; y++)
            for (int x = 0; x <= sx; x++)
                v[y * (sx + 1) + x] = new Vector3((x / (float)sx - 0.5f) * width, (y / (float)sy - 0.5f) * height, 0);
        return v;
    }

    private Vector2[] GenerateGridUVs(int sx, int sy)
    {
        Vector2[] uvs = new Vector2[(sx + 1) * (sy + 1)];
        for (int y = 0; y <= sy; y++)
            for (int x = 0; x <= sx; x++)
                uvs[y * (sx + 1) + x] = new Vector2((float)x / sx, (float)y / sy);
        return uvs;
    }

    public void ResetEditorState()
    {
        CleanupUVPreview();

        w.currentObject = null;
        w.workingMesh = null;
        w.customVertices = null;
        w.customUVs = null;
        w.originalMaterial = null;
        w.originalUVsBackup = null;
        w.editMode = false;
        w.editUVMode = false;
        w.isPreviewInstance = false;
        w.hasUnsavedMeshChanges = false;
        w.selectedIndices.Clear();
        Tools.hidden = false;
    }
}