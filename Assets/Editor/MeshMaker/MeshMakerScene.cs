using UnityEngine;
using UnityEditor;

public class MeshMakerScene
{
    private MeshMakerEditor w;

    public MeshMakerScene(MeshMakerEditor window)
    {
        w = window;
    }

    public void DrawSceneGUI(SceneView sv)
    {
        if (!w.currentObject || w.customVertices == null || w.workingMesh == null) return;

        Matrix4x4 l2w = w.currentObject.transform.localToWorldMatrix;
        Matrix4x4 w2l = w.currentObject.transform.worldToLocalMatrix;

        if (w.editUVMode) w.coreHelper.UpdateUVPreview();

        int hotCtrl = GUIUtility.hotControl;
        if (hotCtrl != 0 && w.lastHotControl == 0) CacheDragState(l2w);
        else if (hotCtrl == 0) w.dragStartVertices = null;
        w.lastHotControl = hotCtrl;

        if (w.editMode)
        {
            Event e = Event.current;
            bool isCtrlPressed = e.control;
            int defaultControlID = GUIUtility.GetControlID(FocusType.Passive);

            // 신규: Q 키를 눌러 뷰 툴(손바닥)을 선택한 상태인지 확인
            bool isViewToolActive = Tools.current == Tool.View;

            if (w.selectedIndices.Count > 0 && e.type == EventType.KeyDown && (e.keyCode == KeyCode.Delete || e.keyCode == KeyCode.Backspace))
            {
                if (e.control || e.command) w.coreHelper.DissolveSelectedVertices();
                else w.coreHelper.RemoveSelectedVertices();
                e.Use();
                w.Repaint();
            }

            // 뷰 툴이 아닐 때만 엣지 추가(Ctrl) 기능 작동
            if (isCtrlPressed && !isViewToolActive)
            {
                HandleEdgeSplit(e, l2w, w2l);
                SceneView.RepaintAll();
            }

            for (int i = 0; i < w.customVertices.Length; i++)
            {
                Vector3 wPos = l2w.MultiplyPoint3x4(w.customVertices[i]);
                float size = HandleUtility.GetHandleSize(wPos) * w.gizmoSize;

                if (isCtrlPressed) Handles.color = Color.gray;
                else Handles.color = w.selectedIndices.Contains(i) ? Color.red : Color.yellow;

                int id = GUIUtility.GetControlID(i, FocusType.Passive);
                Handles.DotHandleCap(id, wPos, Quaternion.identity, size, e.type);

                // 뷰 툴이 아닐 때만 버텍스 클릭 선택 허용
                if (!isViewToolActive && !isCtrlPressed && e.type == EventType.MouseDown && e.button == 0 && HandleUtility.nearestControl == id)
                {
                    if (e.shift || e.control || e.command) { if (w.selectedIndices.Contains(i)) w.selectedIndices.Remove(i); else w.selectedIndices.Add(i); }
                    else { w.selectedIndices.Clear(); w.selectedIndices.Add(i); }
                    GUIUtility.hotControl = id;
                    e.Use();
                    w.Repaint();
                }
            }

            if (!isCtrlPressed && w.selectedIndices.Count > 0 && !isViewToolActive)
            {
                Vector3 currentCenterWPos = Vector3.zero;
                foreach (int idx in w.selectedIndices) currentCenterWPos += l2w.MultiplyPoint3x4(w.customVertices[idx]);
                currentCenterWPos /= w.selectedIndices.Count;

                Vector3 handlePos = (w.dragStartVertices != null) ? w.dragStartCenter : currentCenterWPos;
                Quaternion handleRot = (w.dragStartVertices != null) ? w.dragStartRot : w.currentObject.transform.rotation;

                EditorGUI.BeginChangeCheck();

                if (Tools.current == Tool.Move || Tools.current == Tool.None || Tools.current == Tool.Rect)
                {
                    Vector3 newPos = Handles.PositionHandle(handlePos, handleRot);
                    if (EditorGUI.EndChangeCheck())
                    {
                        if (w.dragStartVertices == null) CacheDragState(l2w);

                        Undo.RecordObject(w, "Move Vertices");
                        Undo.RecordObject(w.workingMesh, "Move Vertices");

                        Vector3 deltaL = w2l.MultiplyVector(newPos - w.dragStartCenter);
                        for (int j = 0; j < w.selectedIndices.Count; j++)
                            w.customVertices[w.selectedIndices[j]] = w.dragStartVertices[w.selectedIndices[j]] + deltaL;

                        if (w.editUVMode) w.coreHelper.ProjectUVs();
                        else w.coreHelper.UpdateMeshChanges();
                    }
                }
                else if (Tools.current == Tool.Rotate)
                {
                    Quaternion newRot = Handles.RotationHandle(handleRot, handlePos);
                    if (EditorGUI.EndChangeCheck())
                    {
                        if (w.dragStartVertices == null) CacheDragState(l2w);

                        Undo.RecordObject(w, "Rotate Vertices");
                        Undo.RecordObject(w.workingMesh, "Rotate Vertices");

                        Quaternion deltaRot = newRot * Quaternion.Inverse(w.dragStartRot);
                        for (int j = 0; j < w.selectedIndices.Count; j++)
                        {
                            int idx = w.selectedIndices[j];
                            Vector3 worldPos = l2w.MultiplyPoint3x4(w.dragStartVertices[idx]);
                            worldPos = w.dragStartCenter + deltaRot * (worldPos - w.dragStartCenter);
                            w.customVertices[idx] = w2l.MultiplyPoint3x4(worldPos);
                        }

                        if (w.editUVMode) w.coreHelper.ProjectUVs();
                        else w.coreHelper.UpdateMeshChanges();
                    }
                }
                else if (Tools.current == Tool.Scale)
                {
                    Vector3 newScale = Handles.ScaleHandle(Vector3.one, handlePos, handleRot, HandleUtility.GetHandleSize(handlePos));
                    if (EditorGUI.EndChangeCheck())
                    {
                        if (w.dragStartVertices == null) CacheDragState(l2w);

                        Undo.RecordObject(w, "Scale Vertices");
                        Undo.RecordObject(w.workingMesh, "Scale Vertices");

                        for (int j = 0; j < w.selectedIndices.Count; j++)
                        {
                            int idx = w.selectedIndices[j];
                            Vector3 worldPos = l2w.MultiplyPoint3x4(w.dragStartVertices[idx]);
                            Vector3 offset = worldPos - w.dragStartCenter;

                            Vector3 localOffset = Quaternion.Inverse(w.dragStartRot) * offset;
                            localOffset.x *= newScale.x;
                            localOffset.y *= newScale.y;
                            localOffset.z *= newScale.z;

                            worldPos = w.dragStartCenter + w.dragStartRot * localOffset;
                            w.customVertices[idx] = w2l.MultiplyPoint3x4(worldPos);
                        }

                        if (w.editUVMode) w.coreHelper.ProjectUVs();
                        else w.coreHelper.UpdateMeshChanges();
                    }
                }
            }

            // 뷰 툴(Q)이 아닐 때만 드래그 박스 선택 허용. 뷰 툴일 때는 유니티 기본 카메라 패닝 기능이 작동함!
            if (!isViewToolActive && !isCtrlPressed && !e.alt && e.button == 0)
            {
                switch (e.type)
                {
                    case EventType.MouseDown:
                        if (HandleUtility.nearestControl == defaultControlID)
                        {
                            w.isDragging = true;
                            w.mouseStartPos = e.mousePosition;
                            if (!e.shift) w.selectedIndices.Clear();
                            GUIUtility.hotControl = defaultControlID;
                            e.Use();
                            w.Repaint();
                        }
                        break;

                    case EventType.MouseDrag:
                        if (w.isDragging) { sv.Repaint(); e.Use(); }
                        break;

                    case EventType.MouseUp:
                        if (w.isDragging)
                        {
                            w.isDragging = false;
                            GUIUtility.hotControl = 0;
                            Rect selectionRect = GetScreenRect(w.mouseStartPos, e.mousePosition);
                            for (int i = 0; i < w.customVertices.Length; i++)
                            {
                                Vector3 worldPos = l2w.MultiplyPoint3x4(w.customVertices[i]);
                                Vector2 guiPos = HandleUtility.WorldToGUIPoint(worldPos);
                                if (selectionRect.Contains(guiPos) && !w.selectedIndices.Contains(i)) w.selectedIndices.Add(i);
                            }
                            e.Use();
                            w.Repaint();
                        }
                        break;
                }
            }

            if (w.isDragging && !isViewToolActive)
            {
                Handles.BeginGUI();
                Rect drawRect = GetScreenRect(w.mouseStartPos, Event.current.mousePosition);
                EditorGUI.DrawRect(drawRect, new Color(0.2f, 0.5f, 1f, 0.3f));
                Handles.EndGUI();
            }

            if (e.type == EventType.Layout) HandleUtility.AddDefaultControl(defaultControlID);
        }

        if (w.editMode || w.editUVMode)
        {
            Handles.color = new Color(0f, 1f, 1f, 0.4f);
            int[] tris = w.workingMesh.triangles;
            Vector3[] verts = w.customVertices;

            for (int i = 0; i < tris.Length; i += 3)
            {
                Vector3 v1 = l2w.MultiplyPoint3x4(verts[tris[i]]);
                Vector3 v2 = l2w.MultiplyPoint3x4(verts[tris[i + 1]]);
                Vector3 v3 = l2w.MultiplyPoint3x4(verts[tris[i + 2]]);

                Handles.DrawLine(v1, v2);
                Handles.DrawLine(v2, v3);
                Handles.DrawLine(v3, v1);
            }
        }
    }

    private void CacheDragState(Matrix4x4 l2w)
    {
        w.dragStartVertices = (Vector3[])w.customVertices.Clone();
        w.dragStartCenter = Vector3.zero;
        if (w.selectedIndices.Count > 0)
        {
            foreach (int idx in w.selectedIndices) w.dragStartCenter += l2w.MultiplyPoint3x4(w.customVertices[idx]);
            w.dragStartCenter /= w.selectedIndices.Count;
        }
        w.dragStartRot = w.currentObject.transform.rotation;
    }

    private Rect GetScreenRect(Vector2 screenPos1, Vector2 screenPos2)
    {
        screenPos1.y = screenPos1.y;
        screenPos2.y = screenPos2.y;
        Vector2 topLeft = Vector2.Min(screenPos1, screenPos2);
        Vector2 bottomRight = Vector2.Max(screenPos1, screenPos2);
        return Rect.MinMaxRect(topLeft.x, topLeft.y, bottomRight.x, bottomRight.y);
    }

    private void HandleEdgeSplit(Event e, Matrix4x4 l2w, Matrix4x4 w2l)
    {
        if (w.workingMesh == null) return;
        int[] triangles = w.workingMesh.triangles;
        Vector3[] verts = w.customVertices;
        float minDst = 10f;
        int edgeA = -1, edgeB = -1;
        Vector3 splitPointW = Vector3.zero;

        for (int i = 0; i < triangles.Length; i += 3)
        {
            for (int j = 0; j < 3; j++)
            {
                Vector3 v1 = l2w.MultiplyPoint3x4(verts[triangles[i + j]]);
                Vector3 v2 = l2w.MultiplyPoint3x4(verts[triangles[i + (j + 1) % 3]]);
                float dst = HandleUtility.DistanceToLine(v1, v2);
                if (dst < minDst)
                {
                    minDst = dst;
                    edgeA = triangles[i + j];
                    edgeB = triangles[i + (j + 1) % 3];
                    splitPointW = HandleUtility.ClosestPointToPolyLine(v1, v2);
                }
            }
        }

        if (edgeA != -1)
        {
            Handles.color = Color.cyan;
            Handles.DrawLine(l2w.MultiplyPoint3x4(verts[edgeA]), l2w.MultiplyPoint3x4(verts[edgeB]), 2f);
            float handleSize = HandleUtility.GetHandleSize(splitPointW) * 0.05f;
            Handles.RectangleHandleCap(0, splitPointW, SceneView.lastActiveSceneView.rotation, handleSize, e.type);

            if (e.type == EventType.MouseDown && e.button == 0)
            {
                w.coreHelper.InsertVertexOnEdge(edgeA, edgeB, w2l.MultiplyPoint3x4(splitPointW));
                e.Use();
                w.Repaint();
            }
        }
    }
}