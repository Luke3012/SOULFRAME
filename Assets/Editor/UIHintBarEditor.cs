using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(UIHintBar))]
public class UIHintBarEditor : Editor
{
    private SerializedProperty hintTextProp;
    private SerializedProperty inspectorContextProp;

    private SerializedProperty arrowsVerticalAssetProp;
    private SerializedProperty arrowsHorizontalAssetProp;
    private SerializedProperty enterAssetProp;
    private SerializedProperty backspaceAssetProp;
    private SerializedProperty escapeAssetProp;
    private SerializedProperty spaceAssetProp;
    private SerializedProperty spaceOutlinedAssetProp;
    private SerializedProperty deleteAssetProp;
    private SerializedProperty anyAssetProp;
    private SerializedProperty insAssetProp;

    private SerializedProperty touchBackAssetProp;
    private SerializedProperty touchKeyboardAssetProp;
    private SerializedProperty touchConfirmAssetProp;
    private SerializedProperty touchCancelAssetProp;
    private SerializedProperty touchMicIdleAssetProp;
    private SerializedProperty touchMicActiveAssetProp;
    private SerializedProperty touchDeleteAssetProp;
    private SerializedProperty touchRestoreAssetProp;
    private SerializedProperty touchTapAssetProp;
    private SerializedProperty touchHoldAssetProp;
    private SerializedProperty touchHoldActiveAssetProp;
    private SerializedProperty touchSwipeHorizontalAssetProp;
    private SerializedProperty touchSwipeVerticalAssetProp;

    private SerializedProperty separatorProp;
    private SerializedProperty useFallbackTextIfMissingProp;

    private void OnEnable()
    {
        hintTextProp = serializedObject.FindProperty("hintText");
        inspectorContextProp = serializedObject.FindProperty("inspectorContext");

        arrowsVerticalAssetProp = serializedObject.FindProperty("arrowsVerticalAsset");
        arrowsHorizontalAssetProp = serializedObject.FindProperty("arrowsHorizontalAsset");
        enterAssetProp = serializedObject.FindProperty("enterAsset");
        backspaceAssetProp = serializedObject.FindProperty("backspaceAsset");
        escapeAssetProp = serializedObject.FindProperty("escapeAsset");
        spaceAssetProp = serializedObject.FindProperty("spaceAsset");
        spaceOutlinedAssetProp = serializedObject.FindProperty("spaceOutlinedAsset");
        deleteAssetProp = serializedObject.FindProperty("deleteAsset");
        anyAssetProp = serializedObject.FindProperty("anyAsset");
        insAssetProp = serializedObject.FindProperty("insAsset");

        touchBackAssetProp = serializedObject.FindProperty("touchBackAsset");
        touchKeyboardAssetProp = serializedObject.FindProperty("touchKeyboardAsset");
        touchConfirmAssetProp = serializedObject.FindProperty("touchConfirmAsset");
        touchCancelAssetProp = serializedObject.FindProperty("touchCancelAsset");
        touchMicIdleAssetProp = serializedObject.FindProperty("touchMicIdleAsset");
        touchMicActiveAssetProp = serializedObject.FindProperty("touchMicActiveAsset");
        touchDeleteAssetProp = serializedObject.FindProperty("touchDeleteAsset");
        touchRestoreAssetProp = serializedObject.FindProperty("touchRestoreAsset");
        touchTapAssetProp = serializedObject.FindProperty("touchTapAsset");
        touchHoldAssetProp = serializedObject.FindProperty("touchHoldAsset");
        touchHoldActiveAssetProp = serializedObject.FindProperty("touchHoldActiveAsset");
        touchSwipeHorizontalAssetProp = serializedObject.FindProperty("touchSwipeHorizontalAsset");
        touchSwipeVerticalAssetProp = serializedObject.FindProperty("touchSwipeVerticalAsset");

        separatorProp = serializedObject.FindProperty("separator");
        useFallbackTextIfMissingProp = serializedObject.FindProperty("useFallbackTextIfMissing");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.PropertyField(hintTextProp);
        EditorGUILayout.PropertyField(inspectorContextProp);

        var context = (UIHintBar.InspectorContext)inspectorContextProp.enumValueIndex;

        if (context != UIHintBar.InspectorContext.TouchOnly)
        {
            DrawDesktopAssets();
        }

        if (context != UIHintBar.InspectorContext.DesktopOnly)
        {
            DrawTouchAssets();
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Formatting", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(separatorProp);
        EditorGUILayout.PropertyField(useFallbackTextIfMissingProp);

        serializedObject.ApplyModifiedProperties();
    }

    private void DrawDesktopAssets()
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Desktop Sprite Assets", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(arrowsVerticalAssetProp);
        EditorGUILayout.PropertyField(arrowsHorizontalAssetProp);
        EditorGUILayout.PropertyField(enterAssetProp);
        EditorGUILayout.PropertyField(backspaceAssetProp);
        EditorGUILayout.PropertyField(escapeAssetProp);
        EditorGUILayout.PropertyField(spaceAssetProp);
        EditorGUILayout.PropertyField(spaceOutlinedAssetProp);
        EditorGUILayout.PropertyField(deleteAssetProp);
        EditorGUILayout.PropertyField(anyAssetProp);
        EditorGUILayout.PropertyField(insAssetProp);
    }

    private void DrawTouchAssets()
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Touch Sprite Assets", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(touchBackAssetProp);
        EditorGUILayout.PropertyField(touchKeyboardAssetProp);
        EditorGUILayout.PropertyField(touchConfirmAssetProp);
        EditorGUILayout.PropertyField(touchCancelAssetProp);
        EditorGUILayout.PropertyField(touchMicIdleAssetProp);
        EditorGUILayout.PropertyField(touchMicActiveAssetProp);
        EditorGUILayout.PropertyField(touchDeleteAssetProp);
        EditorGUILayout.PropertyField(touchRestoreAssetProp);
        EditorGUILayout.PropertyField(touchTapAssetProp);
        EditorGUILayout.PropertyField(touchHoldAssetProp);
        EditorGUILayout.PropertyField(touchHoldActiveAssetProp);
        EditorGUILayout.PropertyField(touchSwipeHorizontalAssetProp);
        EditorGUILayout.PropertyField(touchSwipeVerticalAssetProp);
    }
}
