using UnityEngine;
using UnityEditor;

public class MeshMakerUI
{
    private MeshMakerEditor w;

    public MeshMakerUI(MeshMakerEditor window)
    {
        w = window;
    }

    public void DrawUI()
    {
        w.scrollPosition = EditorGUILayout.BeginScrollView(w.scrollPosition);

        if (w.currentObject == null && (w.editMode || w.isPreviewInstance)) w.coreHelper.ResetEditorState();

        GUILayout.Label("Mesh Maker", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        // --- Section 1: Setup ---
        bool disableCreateUI = w.currentObject != null && !w.isPreviewInstance;
        EditorGUI.BeginDisabledGroup(disableCreateUI);
        {
            GUILayout.Label("1. Setup Grid", EditorStyles.miniBoldLabel);
            w.meshName = EditorGUILayout.TextField("Name", w.meshName);
            EditorGUI.BeginChangeCheck();
            w.segmentsX = Mathf.Max(1, EditorGUILayout.IntField("Segments X", w.segmentsX));
            w.segmentsY = Mathf.Max(1, EditorGUILayout.IntField("Segments Y", w.segmentsY));
            w.width = EditorGUILayout.FloatField("Width", w.width);
            w.height = EditorGUILayout.FloatField("Height", w.height);
            if (EditorGUI.EndChangeCheck())
            {
                if (w.isPreviewInstance)
                {
                    w.coreHelper.ResetPreviewVertices();
                    w.hasUnsavedMeshChanges = true;
                    SceneView.RepaintAll();
                }
            }
            if (GUILayout.Button(w.isPreviewInstance ? "Refresh Preview" : "Spawn New Preview", GUILayout.Height(30)))
                w.coreHelper.SpawnNewMesh();
        }
        EditorGUI.EndDisabledGroup();

        EditorGUILayout.Space(5);
        GUILayout.Box("", GUILayout.ExpandWidth(true), GUILayout.Height(1));
        EditorGUILayout.Space(5);

        if (w.currentObject != null)
        {
            if (w.editMode || w.editUVMode)
                GUILayout.Label($"Editing: {w.currentObject.name} (Selected: {w.selectedIndices.Count} Verts)", EditorStyles.helpBox);

            // ==========================================
            // 1. UV 에디터 모드 영역
            // ==========================================
            GUILayout.BeginVertical("box");
            EditorGUI.BeginChangeCheck();
            w.editUVMode = EditorGUILayout.ToggleLeft(" Edit UV Mode (Projection)", w.editUVMode, EditorStyles.boldLabel);

            if (EditorGUI.EndChangeCheck())
            {
                if (w.editUVMode)
                {
                    if (w.customUVs != null) w.originalUVsBackup = (Vector2[])w.customUVs.Clone();
                    w.coreHelper.ResetUVProjectionBounds();
                    w.coreHelper.ProjectUVs();
                }
                else
                {
                    if (w.originalUVsBackup != null)
                    {
                        if (w.originalUVsBackup.Length == w.customVertices.Length)
                        {
                            w.customUVs = (Vector2[])w.originalUVsBackup.Clone();
                        }
                        w.coreHelper.UpdateMeshChanges();
                    }
                    w.coreHelper.CleanupUVPreview();
                }
                SceneView.RepaintAll();
            }

            if (w.editUVMode)
            {
                EditorGUILayout.Space(2);
                EditorGUI.BeginChangeCheck();
                w.uvTiling = EditorGUILayout.Slider("UV Tiling (1 = Fit Object)", w.uvTiling, 0.1f, 10.0f);
                w.tempTexture = (Texture2D)EditorGUILayout.ObjectField("Temp Texture", w.tempTexture, typeof(Texture2D), false);

                if (EditorGUI.EndChangeCheck())
                {
                    w.coreHelper.ProjectUVs();
                    w.coreHelper.UpdateUVPreview();
                }

                EditorGUILayout.Space(5);

                EditorGUILayout.BeginHorizontal();
                GUI.backgroundColor = Color.green;
                if (GUILayout.Button("Apply UV Projection (UV 적용)", GUILayout.Height(30)))
                {
                    if (w.customUVs != null) w.originalUVsBackup = (Vector2[])w.customUVs.Clone();
                    w.hasUnsavedMeshChanges = true;
                    Debug.Log("[MeshMaker] UV가 메쉬에 적용되었습니다!");
                }
                GUI.backgroundColor = Color.white;

                if (GUILayout.Button("Reset UV Scale (Fit)", GUILayout.Height(30)))
                {
                    w.uvTiling = 1.0f;
                    w.coreHelper.ResetUVProjectionBounds();
                    w.coreHelper.ProjectUVs();
                    w.coreHelper.UpdateUVPreview();
                    SceneView.RepaintAll();
                }
                EditorGUILayout.EndHorizontal();
            }
            GUILayout.EndVertical();

            EditorGUILayout.Space(2);
            GUILayout.Box("", GUILayout.ExpandWidth(true), GUILayout.Height(2));
            EditorGUILayout.Space(2);

            // ==========================================
            // 2. 버텍스 에디터 모드 영역
            // ==========================================
            GUILayout.BeginVertical("box");
            EditorGUI.BeginChangeCheck();
            w.editMode = EditorGUILayout.ToggleLeft(" Edit Vertex Mode", w.editMode, EditorStyles.boldLabel);

            if (EditorGUI.EndChangeCheck())
            {
                if (w.editMode) { Tools.hidden = true; }
                else { w.selectedIndices.Clear(); Tools.hidden = false; }
                SceneView.RepaintAll();
            }

            if (w.editMode)
            {
                EditorGUILayout.Space(2);
                EditorGUI.BeginChangeCheck();
                w.gizmoSize = EditorGUILayout.Slider("Vertex Gizmo Size", w.gizmoSize, 0.01f, 0.5f);
                if (EditorGUI.EndChangeCheck()) SceneView.RepaintAll();

                EditorGUILayout.Space(5);

                EditorGUILayout.BeginHorizontal();
                EditorGUI.BeginDisabledGroup(w.selectedIndices.Count != 2);
                GUI.backgroundColor = new Color(0.2f, 0.8f, 1f);
                if (GUILayout.Button($"Connect Edges\n(엣지 연결)", GUILayout.Height(40))) w.coreHelper.ConnectSelectedVertices();
                EditorGUI.EndDisabledGroup();

                EditorGUI.BeginDisabledGroup(w.selectedIndices.Count == 0);
                GUI.backgroundColor = new Color(1f, 0.4f, 0.4f);
                if (GUILayout.Button($"Dissolve Vertices\n(선택 면 유지 삭제)", GUILayout.Height(40))) w.coreHelper.DissolveSelectedVertices();
                EditorGUI.EndDisabledGroup();
                GUI.backgroundColor = Color.white;
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.Space(2);

                EditorGUILayout.BeginHorizontal();
                EditorGUI.BeginDisabledGroup(w.selectedIndices.Count < 2);
                GUI.backgroundColor = new Color(1f, 0.6f, 0.2f);
                if (GUILayout.Button($"Merge Selected\n(선택 버텍스 합치기)", GUILayout.Height(40))) w.coreHelper.MergeSelectedVertices();
                EditorGUI.EndDisabledGroup();

                GUI.backgroundColor = new Color(0.9f, 0.8f, 0.2f);
                if (GUILayout.Button($"Auto Merge All\n(근접 자동 합치기)", GUILayout.Height(40))) w.coreHelper.AutoMergeVertices();
                GUI.backgroundColor = Color.white;
                EditorGUILayout.EndHorizontal();

                EditorGUI.BeginChangeCheck();
                w.autoMergeDistance = EditorGUILayout.Slider("Auto Merge Distance", w.autoMergeDistance, 0.001f, 0.5f);
                if (EditorGUI.EndChangeCheck()) SceneView.RepaintAll();

                EditorGUILayout.Space(5);
                GUILayout.Label("Alpha Texture Auto-Snap (외곽선 자동 맞춤)", EditorStyles.boldLabel);
                EditorGUI.BeginChangeCheck();
                w.alphaPadding = EditorGUILayout.Slider("Padding (벌림 간격)", w.alphaPadding, 0.0f, 1.0f);
                if (EditorGUI.EndChangeCheck()) SceneView.RepaintAll();

                bool disableAlphaSnap = !w.editUVMode || w.tempTexture == null;
                EditorGUI.BeginDisabledGroup(disableAlphaSnap);
                GUI.backgroundColor = disableAlphaSnap ? Color.white : new Color(0.6f, 0.9f, 0.6f);
                if (GUILayout.Button("버텍스 위치 자동 조절 (Alpha Snap)", GUILayout.Height(35)))
                {
                    w.coreHelper.AutoAdjustVerticesToAlpha();
                }
                GUI.backgroundColor = Color.white;
                EditorGUI.EndDisabledGroup();

                EditorGUILayout.Space(5);
                EditorGUILayout.HelpBox("💡 Q 키: 카메라 이동 / W,E,R 키: 버텍스 편집\n💡 Ctrl+클릭: 엣지 추가 / Del: 구멍뚫기 / Ctrl+Del: 용해 삭제", MessageType.Info);

                EditorGUILayout.Space(5);
                if (GUILayout.Button("Cancel Vertex Changes (수정 초기화)", GUILayout.Height(25)))
                {
                    w.coreHelper.CancelVertexChanges();
                    w.hasUnsavedMeshChanges = false;
                    SceneView.RepaintAll();
                }
            }
            GUILayout.EndVertical();

            // ==========================================
            // 💡 저장 영역 구분선 추가
            // ==========================================
            EditorGUILayout.Space(2);
            GUILayout.Box("", GUILayout.ExpandWidth(true), GUILayout.Height(2));
            EditorGUILayout.Space(2);

            // ==========================================
            // 3. 저장 영역
            // ==========================================
            w.saveFolder = (DefaultAsset)EditorGUILayout.ObjectField("Save Folder", w.saveFolder, typeof(DefaultAsset), false);

            bool canSave = w.hasUnsavedMeshChanges || w.editMode || w.editUVMode || w.isPreviewInstance;
            EditorGUI.BeginDisabledGroup(!canSave);
            {
                EditorGUILayout.BeginHorizontal();
                GUI.backgroundColor = Color.yellow;
                EditorGUI.BeginDisabledGroup(w.isPreviewInstance && w.originalMeshAsset == null);
                if (GUILayout.Button("Overwrite", GUILayout.Height(40))) w.coreHelper.OverwriteExisting();
                EditorGUI.EndDisabledGroup();

                GUI.backgroundColor = Color.cyan;
                if (GUILayout.Button("Save As New", GUILayout.Height(40))) w.coreHelper.SaveAsNew();
                EditorGUILayout.EndHorizontal();
                GUI.backgroundColor = Color.white;
            }
            EditorGUI.EndDisabledGroup();
        }

        EditorGUILayout.EndScrollView();
    }
}