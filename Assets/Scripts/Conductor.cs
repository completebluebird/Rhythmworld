using System;
using UnityEngine;

// 오디오 DSP 시간을 기준으로 박자를 세는 박자 시계.
// 박 번호는 1부터 센다: 1박 = 0.14초(첫 박), 10박 = 5.54초(음악 진입), 13박 = 7.34초(첫 도장).
public class Conductor : MonoBehaviour
{
    public static Conductor Instance { get; private set; }

    [Header("오디오")]
    public AudioSource bgmSource;       // bgmmake.wav
    public AudioSource ambienceSource;  // keyboardsound.wav (Loop 체크)
    [Range(0f, 1f)] public float ambienceVolume = 0.18f;

    [Header("박자")]
    public float bpm = 99.97f;
    public float firstBeatOffset = 0.14f;  // 첫 박이 나오는 시간(초)
    public float startDelay = 1.0f;        // 시작 전 여유 시간(초)

    public event Action<int> OnBeat;       // 새 박이 시작될 때 박 번호를 알려줌

    public float SecPerBeat => 60f / bpm;
    public bool IsPlaying { get; private set; }

    // 곡 시작 기준 현재 시간(초). 시작 전에는 음수.
    public double SongTime => IsPlaying ? AudioSettings.dspTime - dspSongStart : 0;

    // 현재 박 위치(소수 포함). 1.0 = 첫 박, 1.5 = 첫 박과 둘째 박 사이.
    public double SongBeat => (SongTime - firstBeatOffset) / SecPerBeat + 1.0;

    double dspSongStart;
    int lastBeat;

    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        StartSong();
    }

    public void StartSong()
    {
        dspSongStart = AudioSettings.dspTime + startDelay;
        bgmSource.PlayScheduled(dspSongStart);

        if (ambienceSource != null)
        {
            ambienceSource.loop = true;
            ambienceSource.volume = ambienceVolume;
            ambienceSource.Play();
        }

        lastBeat = 0;
        IsPlaying = true;
    }

    // 박 번호를 곡 시간(초)으로 바꿈. 노트 판정에 사용.
    public double BeatToTime(double beat)
    {
        return firstBeatOffset + (beat - 1.0) * SecPerBeat;
    }

    void Update()
    {
        if (!IsPlaying || SongTime < 0) return;

        int currentBeat = (int)Math.Floor(SongBeat);
        while (lastBeat < currentBeat)
        {
            lastBeat++;
            OnBeat?.Invoke(lastBeat);
        }
    }
}
