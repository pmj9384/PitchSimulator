using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(SoundManager))]
public class SoundManagerEditor : Editor
{
    private int channelCount = 16;
    
    public override void OnInspectorGUI()
    {
        base.OnInspectorGUI();
        
        var soundManager = (SoundManager)target;
        
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Add Audio Source", EditorStyles.boldLabel);
        
        channelCount = EditorGUILayout.IntField("Channel Count to Add", channelCount);
        
        if (GUILayout.Button($"Add AudioSource"))
        {
            if (soundManager.audioSourcePlayer is null)
            {
                Debug.LogError("Missing AudioSource Parent");
                
                return;
            }
            
            var components = soundManager.audioSourcePlayer.GetComponents<AudioSource>();
            foreach (var component in components)
            {
                Undo.DestroyObjectImmediate(component);
            }

            soundManager.bgmAudioSource = soundManager.audioSourcePlayer.AddComponent<AudioSource>();
            soundManager.bgmAudioSource.outputAudioMixerGroup = soundManager.audioMixer.FindMatchingGroups("Master")[1];

            soundManager.sfxAudioSourceList.Clear();
            for (int i = 0; i < channelCount; ++i)
            {
                soundManager.sfxAudioSourceList.Add(soundManager.audioSourcePlayer.AddComponent<AudioSource>());
                soundManager.sfxAudioSourceList[i].outputAudioMixerGroup = soundManager.audioMixer.FindMatchingGroups("Master")[2];
            }
            
            EditorUtility.SetDirty(soundManager);
        }
    }
}