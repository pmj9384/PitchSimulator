using AYellowpaper.SerializedCollections;
using System.Collections.Generic;
using UnityCommunity.UnitySingleton;
using UnityEngine;
using UnityEngine.Audio;

public class SoundManager : PersistentMonoSingleton<SoundManager>
{
    public AudioMixer audioMixer;

    [SerializedDictionary("BgmClipId", "AudioClip")]
    [SerializeField] private SerializedDictionary<BgmClipId, AudioClip> bgmClips;
    [ReadOnly] public AudioSource bgmAudioSource;
    public float BgmVolume { get; private set; }

    [SerializedDictionary("SfxClipId", "AudioClip")]
    [SerializeField] private SerializedDictionary<SfxClipId, AudioClip> sfxClips;
    [ReadOnly] public List<AudioSource> sfxAudioSourceList;
    public float SfxVolume { get; private set; }
    private int channelIndex;
    public GameObject audioSourcePlayer;

    protected override void Awake()
    {
        if (audioSourcePlayer != null)
        {
            audioSourcePlayer.transform.SetParent(transform);
        }

        base.Awake();
    }

    private void Start()
    {
        BgmVolume = GameDataManager.Instance.PlayerAccountData.BgmVolume;
        SfxVolume = GameDataManager.Instance.PlayerAccountData.SfxVolume;
        channelIndex = 0;

        bgmAudioSource.loop = true;
        bgmAudioSource.playOnAwake = true;
        bgmAudioSource.volume = BgmVolume;

        foreach (var sfxAudioSource in sfxAudioSourceList)
        {
            sfxAudioSource.loop = false;
            sfxAudioSource.playOnAwake = false;
            sfxAudioSource.volume = SfxVolume;
        }

        audioSourcePlayer.transform.SetParent(transform);
    }

    public void PlayBgm(BgmClipId clipId)
    {
        if (!bgmClips.ContainsKey(clipId))
        {
            Debug.Assert(false, $"BgmClipId {clipId} is not found in bgmClips");
            return;
        }

        PlayBgm(bgmClips[clipId]);
    }

    private void PlayBgm(AudioClip clip)
    {
        if (bgmAudioSource.isPlaying)
        {
            bgmAudioSource.Stop();
        }

        bgmAudioSource.clip = clip;
        bgmAudioSource.Play();
    }

    public void StopBgm() => bgmAudioSource.Stop();
    public void PauseBgm() => bgmAudioSource.Pause();
    public void ResumeBgm() => bgmAudioSource.UnPause();

    public void PlaySfx(SfxClipId clipId)
    {
        if (clipId == SfxClipId.None) { return; }

        if (!sfxClips.ContainsKey(clipId))
        {
            Debug.Assert(false, $"SfxClipId {clipId} is not found in sfxClips");
            return;
        }

        PlaySfx(sfxClips[clipId]);
    }

    private void PlaySfx(AudioClip clip)
    {
        for (int i = 0; i < sfxAudioSourceList.Count; ++i)
        {
            int loopIndex = (i + channelIndex) % sfxAudioSourceList.Count;

            if (sfxAudioSourceList[loopIndex].isPlaying) { continue; }

            sfxAudioSourceList[loopIndex].clip = clip;
            channelIndex = loopIndex;
            sfxAudioSourceList[loopIndex].Play();
            break;
        }
    }

    public void PlaySfxLoop(SfxClipId clipId)
    {
        if (!sfxClips.ContainsKey(clipId)) { return; }
        sfxAudioSourceList[0].clip = sfxClips[clipId];
        sfxAudioSourceList[0].loop = true;
        sfxAudioSourceList[0].Play();
    }

    public void StopSfxLoop()
    {
        sfxAudioSourceList[0].loop = false;
        sfxAudioSourceList[0].Stop();
    }

    public void StopSfx()
    {
        foreach (var src in sfxAudioSourceList) src.Stop();
    }

    public void PauseSfx()
    {
        foreach (var src in sfxAudioSourceList) src.Pause();
    }

    public void ResumeSfx()
    {
        foreach (var src in sfxAudioSourceList) src.UnPause();
    }

    public void SetBgmVolume(float volume)
    {
        BgmVolume = Mathf.Clamp(volume, 0.0001f, 1f);
        bgmAudioSource.volume = BgmVolume;
    }

    public void SetSfxVolume(float volume)
    {
        SfxVolume = Mathf.Clamp(volume, 0.0001f, 1f);
        foreach (var src in sfxAudioSourceList) src.volume = SfxVolume;
    }
}
