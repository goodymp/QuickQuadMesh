using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

public class MeshMakerEditor : EditorWindow
{
    // --- Settings ---
    public string meshName = "NewQuadMesh";
    public int segmentsX = 2, segmentsY = 2;
    public float width = 1f, height = 1f;
    public DefaultAsset saveFolder;

    [Range(0.01f, 0.5f)] public float gizmoSize = 0.1f;
    public bool editUVMode = false;
    public float uvTiling = 1.0f;
    public Texture2D tempTexture;

    // --- Editing State ---
    public GameObject currentObject;
    public Mesh workingMesh;
    public Vector3[] customVertices;
    public Vector2[] customUVs;
    public bool editMode = false;

    [SerializeField] public List<int> selectedIndices = new List<int>();
    public bool isDragging = false;
    public Vector2 mouseStartPos;

    public int lastHotControl = 0;
    public Vector3[] dragStartVertices;
    public Vector3 dragStartCenter;
    public Quaternion dragStartRot;

    public float cachedUVProjectionSize = 0f;
    public Vector3 cachedUVProjectionCenter = Vector3.zero;

    public Material originalMaterial;
    public Vector2[] originalUVsBackup;
    public Vector2 scrollPosition = Vector2.zero;

    public float alphaPadding = 0.05f;
    public float autoMergeDistance = 0.05f; // 신규: 자동 병합 탐지 거리

    public bool hasUnsavedMeshChanges = false;
    public bool isPreviewInstance = false;
    public Mesh originalMeshAsset;
    public GameObject uvPreviewObj;

    // --- Helper Classes ---
    public MeshMakerUI uiHelper;
    public MeshMakerScene sceneHelper;
    public MeshMakerCore coreHelper;

    [MenuItem("Tools/Mesh Maker")]
    public static void ShowWindow() => GetWindow<MeshMakerEditor>("Mesh Maker");

    private void OnEnable()
    {
        uiHelper = new MeshMakerUI(this);
        sceneHelper = new MeshMakerScene(this);
        coreHelper = new MeshMakerCore(this);

        SceneView.duringSceneGui += OnSceneGUI;
        Undo.undoRedoPerformed += OnUndoRedo;
    }

    private void OnDisable()
    {
        if (coreHelper != null) coreHelper.CleanupUVPreview();
        SceneView.duringSceneGui -= OnSceneGUI;
        Undo.undoRedoPerformed -= OnUndoRedo;
        Tools.hidden = false;
    }

    private void OnUndoRedo()
    {
        if (currentObject != null && workingMesh != null)
        {
            customVertices = workingMesh.vertices;
            customUVs = workingMesh.uv;
            workingMesh.RecalculateNormals();
            workingMesh.RecalculateBounds();
            coreHelper.ForceUpdateMeshFilter();
            Repaint();
            SceneView.RepaintAll();
        }
    }

    private void OnSelectionChange()
    {
        if (currentObject != null && (editMode || editUVMode))
        {
            if (Selection.activeGameObject != currentObject)
            {
                EditorApplication.delayCall += () =>
                {
                    if (currentObject != null) Selection.activeGameObject = currentObject;
                };
            }
            return;
        }

        GameObject selected = Selection.activeGameObject;
        if (selected == null) coreHelper.ResetEditorState();
        else if (selected != currentObject)
        {
            if (selected.GetComponent<MeshFilter>() != null) coreHelper.StartEditing(selected);
            else coreHelper.ResetEditorState();
        }
        Repaint();
    }

    private void OnGUI()
    {
        if (uiHelper != null) uiHelper.DrawUI();
    }

    private void OnSceneGUI(SceneView sv)
    {
        if (sceneHelper != null) sceneHelper.DrawSceneGUI(sv);
    }
}