using UnityEngine;

// 박자 확인용 테스트: 박마다 오브젝트가 커졌다 돌아온다.
public class BeatPulse : MonoBehaviour
{
    public float pulseScale = 1.3f;
    public float returnSpeed = 8f;
    public bool logBeats = true;

    Vector3 baseScale;

    void Start()
    {
        baseScale = transform.localScale;
        Conductor.Instance.OnBeat += HandleBeat;
    }

    void OnDestroy()
    {
        if (Conductor.Instance != null) Conductor.Instance.OnBeat -= HandleBeat;
    }

    void HandleBeat(int beat)
    {
        transform.localScale = baseScale * pulseScale;
        if (logBeats) Debug.Log($"{beat}박 ({Conductor.Instance.SongTime:F2}초)");
    }

    void Update()
    {
        transform.localScale = Vector3.Lerp(transform.localScale, baseScale, Time.deltaTime * returnSpeed);
    }
}
