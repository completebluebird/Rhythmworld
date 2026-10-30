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
    [Tooltip("곡 끝 시간(초). 파일 뒤에 무음이 있으면 소리가 끝나는 시간을 적는다. 0이면 파일 길이 사용")]
    public float songEndTime = 0f;

    [Header("박자")]
    public float bpm = 99.97f;
    public float firstBeatOffset = 0.14f;  // 첫 박이 나오는 시간(초)
    public float startDelay = 1.0f;        // 시작 전 여유 시간(초)
    public bool playOnStart = true;        // StampController가 있으면 그쪽에서 시작함

    public event Action<int> OnBeat;       // 새 박이 시작될 때 박 번호를 알려줌

    public float SecPerBeat => 60f / bpm;
    public bool IsPlaying { get; private set; }
    public double DspSongStart => dspSongStart;

    // 곡 시작 기준 현재 시간(초). 시작 전에는 음수. 오디오 버퍼 단위로 갱신되어 계단식으로 움직임.
    public double SongTime => IsPlaying ? AudioSettings.dspTime - dspSongStart : 0;

    // 판정용 곡 시간(초). DSP 시간이 갱신되지 않는 사이를 실제 시간으로 메워서 더 촘촘함.
    public double SongTimeNow
    {
        get
        {
            if (!IsPlaying) return 0;
            double now = lastDsp + (Time.realtimeSinceStartupAsDouble - lastDspRealtime);
            return Math.Max(now, AudioSettings.dspTime) - dspSongStart;
        }
    }

    // 현재 박 위치(소수 포함). 1.0 = 첫 박, 1.5 = 첫 박과 둘째 박 사이.
    public double SongBeat => TimeToBeat(SongTime);

    // 곡이 끝나는 시간(초). songEndTime이 0이면 파일 길이.
    public float SongLength => songEndTime > 0f ? songEndTime
        : bgmSource != null && bgmSource.clip != null ? bgmSource.clip.length : 0f;

    // 곡에 들어 있는 마지막 박 번호
    public int LastBeat => SongLength > 0f ? (int)Math.Floor(TimeToBeat(SongLength)) : 0;

    public bool SongFinished => IsPlaying && SongLength > 0f && SongTime > SongLength;

    double dspSongStart;
    double lastDsp;
    double lastDspRealtime;
    int lastBeat;

    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        if (playOnStart) StartSong();
    }

    // 처음부터 다시 시작할 때도 이걸 부른다
    public void StartSong()
    {
        bgmSource.Stop();
        dspSongStart = AudioSettings.dspTime + startDelay;
        bgmSource.PlayScheduled(dspSongStart);

        if (ambienceSource != null && !ambienceSource.isPlaying)
        {
            ambienceSource.loop = true;
            ambienceSource.volume = ambienceVolume;
            ambienceSource.Play();
        }

        lastDsp = AudioSettings.dspTime;
        lastDspRealtime = Time.realtimeSinceStartupAsDouble;
        lastBeat = 0;
        IsPlaying = true;
    }

    // 박 번호를 곡 시간(초)으로 바꿈. 노트 판정에 사용.
    public double BeatToTime(double beat)
    {
        return firstBeatOffset + (beat - 1.0) * SecPerBeat;
    }

    public double TimeToBeat(double time)
    {
        return (time - firstBeatOffset) / SecPerBeat + 1.0;
    }

    void Update()
    {
        double dsp = AudioSettings.dspTime;
        if (dsp != lastDsp)
        {
            lastDsp = dsp;
            lastDspRealtime = Time.realtimeSinceStartupAsDouble;
        }

        if (!IsPlaying || SongTime < 0) return;

        int currentBeat = (int)Math.Floor(SongBeat);
        while (lastBeat < currentBeat)
        {
            lastBeat++;
            OnBeat?.Invoke(lastBeat);
        }
    }
}
