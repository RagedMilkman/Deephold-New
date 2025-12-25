using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(BehaviourModule))]
public class BehaviourModuleEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        var module = (BehaviourModule)target;

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Runtime State", EditorStyles.boldLabel);
        using (new EditorGUI.DisabledScope(true))
        {
            var activeBehaviour = module.ActiveBehaviour;
            var behaviourName = activeBehaviour ? activeBehaviour.name : "None";
            var intentName = activeBehaviour ? activeBehaviour.IntentType.ToString() : "None";

            EditorGUILayout.TextField("Current Behaviour", behaviourName);
            EditorGUILayout.TextField("Current Intent", intentName);
        }

        if (Application.isPlaying)
            Repaint();
    }
}
