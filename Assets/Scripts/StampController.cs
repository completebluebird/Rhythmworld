using System.Collections;
using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

// 도장찍기 프로토타입: 채보의 박에 맞춰 버튼 하나로 도장을 찍는다.
// 조작: 스페이스/마우스 왼쪽 = 도장, 1~4 = 채보 바꾸고 처음부터, R = 처음부터,
//       G = 가이드 틱 켜기/끄기, [ ] = 입력 보정 -10ms/+10ms
// 서류·도장 오브젝트를 비워 두면 임시 도형을 자동으로 만든다(씬 배치는 나중에 에셋으로 교체).
public class StampController : MonoBehaviour
{
    public Conductor conductor;

    [Header("채보 (비워 두면 1박/2박/4박/혼합 기본 패턴 사용)")]
    public List<StampChart> charts = new List<StampChart>();
    public int chartIndex = 1;

    [Header("판정 폭 (초, 임시값)")]
    public float perfectWindow = 0.045f;
    public float goodWindow = 0.10f;

    [Tooltip("입력 보정(초). 결과의 평균 오차를 보고 맞춘다. +면 늦게 누르는 버릇을 봐준다")]
    public float inputOffset = 0f;

    [Header("오브젝트 (비우면 임시 도형 생성)")]
    public Transform stamp;
    public GameObject paperPrefab;
    public Vector3 stampPoint = new Vector3(0f, 0f, 0.02f);
    public Vector3 paperEnter = new Vector3(0f, 0.001f, 0.35f);   // 인턴 쪽(화면 위)
    public Vector3 paperExit = new Vector3(-0.52f, 0.02f, 0f);    // 왼쪽 서류함
    public float stampRestHeight = 0.18f;
    public bool setupCamera = true;

    [Header("소리 (비우면 임시 소리 생성)")]
    public AudioClip stampClip;
    public AudioClip guideTickClip;
    public bool guideTick = true;   // 찍을 박마다 '틱' 소리로 정답 타이밍을 들려줌(비교용)

    enum Judge { Perfect, Good, Miss }

    List<double> noteTimes = new List<double>();
    int nextNote;
    int nextTick;

    int perfect, good, miss, emptyPress;
    double offsetSum;
    int offsetCount;
    string lastJudgeText = "";
    Color lastJudgeColor = Color.white;

    Transform currentPaper;
    readonly List<Transform> pile = new List<Transform>();
    float stampPressTimer;

    AudioSource sfxSource;
    AudioSource[] tickSources;
    int tickSourceIndex;

    GUIStyle hudStyle;

    void Awake()
    {
        if (conductor == null) conductor = FindAnyObjectByType<Conductor>();
        conductor.playOnStart = false;

        if (charts.Count == 0)
        {
            charts.Add(StampChart.CreatePreset("1박마다", "x"));
            charts.Add(StampChart.CreatePreset("2박마다", "x."));
            charts.Add(StampChart.CreatePreset("4박마다", "x..."));
            charts.Add(StampChart.CreatePreset("혼합", "x.x.xxx."));
        }
        chartIndex = Mathf.Clamp(chartIndex, 0, charts.Count - 1);

        sfxSource = gameObject.AddComponent<AudioSource>();
        sfxSource.playOnAwake = false;
        tickSources = new AudioSource[4];
        for (int i = 0; i < tickSources.Length; i++)
        {
            tickSources[i] = gameObject.AddComponent<AudioSource>();
            tickSources[i].playOnAwake = false;
            tickSources[i].volume = 0.5f;
        }
        if (stampClip == null) stampClip = MakeClip("StampThump", 110f, 0.09f, 0.45f);
        if (guideTickClip == null) guideTickClip = MakeClip("GuideTick", 1600f, 0.03f, 0f);

        if (stamp == null) stamp = CreatePlaceholderScene();
        if (setupCamera && Camera.main != null)
        {
            Camera.main.transform.position = new Vector3(0f, 1.1f, -0.62f);
            Camera.main.transform.rotation = Quaternion.Euler(60f, 0f, 0f);
            Camera.main.fieldOfView = 50f;
            Camera.main.nearClipPlane = 0.05f;
        }
    }

    void Start()
    {
        Restart();
    }

    public void Restart()
    {
        StopAllCoroutines();
        foreach (var p in pile) if (p != null) Destroy(p.gameObject);
        pile.Clear();
        if (currentPaper != null) Destroy(currentPaper.gameObject);
        foreach (var src in tickSources) src.Stop();

        conductor.StartSong();

        noteTimes.Clear();
        foreach (var beat in charts[chartIndex].BuildBeats(conductor.LastBeat))
            noteTimes.Add(conductor.BeatToTime(beat));

        nextNote = 0;
        nextTick = 0;
        perfect = good = miss = emptyPress = 0;
        offsetSum = 0;
        offsetCount = 0;
        lastJudgeText = "";

        currentPaper = SpawnPaper(stampPoint);
    }

    void Update()
    {
        ReadInput(out bool press, out int chartKey, out bool restart, out bool toggleTick, out int offsetStep);

        if (chartKey >= 0 && chartKey < charts.Count)
        {
            chartIndex = chartKey;
            Restart();
            return;
        }
        if (restart) { Restart(); return; }
        if (toggleTick) guideTick = !guideTick;
        if (offsetStep != 0) inputOffset += offsetStep * 0.01f;

        if (press) OnPress(conductor.SongTimeNow - inputOffset);

        // 판정 폭을 지나도록 안 찍은 서류는 Miss
        double now = conductor.SongTimeNow - inputOffset;
        while (nextNote < noteTimes.Count && now > noteTimes[nextNote] + goodWindow)
        {
            ApplyJudge(Judge.Miss, 0);
            nextNote++;
        }

        ScheduleGuideTicks();
        AnimateStamp();
    }

    void OnPress(double pressTime)
    {
        stampPressTimer = 0.08f;
        sfxSource.PlayOneShot(stampClip);

        if (nextNote >= noteTimes.Count) { emptyPress++; return; }

        double delta = pressTime - noteTimes[nextNote];
        double abs = System.Math.Abs(delta);
        if (abs > goodWindow)
        {
            emptyPress++;   // 서류가 없을 때 찍음
            lastJudgeText = "헛도장";
            lastJudgeColor = Color.gray;
            return;
        }

        offsetSum += delta;
        offsetCount++;
        ApplyJudge(abs <= perfectWindow ? Judge.Perfect : Judge.Good, delta);
        nextNote++;
    }

    void ApplyJudge(Judge judge, double delta)
    {
        int ms = (int)System.Math.Round(delta * 1000);
        string timing = judge == Judge.Miss ? "" : ms > 0 ? $" (+{ms}ms 늦음)" : ms < 0 ? $" ({ms}ms 빠름)" : " (0ms)";

        switch (judge)
        {
            case Judge.Perfect:
                perfect++;
                lastJudgeColor = new Color(1f, 0.35f, 0.3f);
                AddMark(currentPaper, new Color(0.8f, 0.08f, 0.08f), 0f, 1f);
                break;
            case Judge.Good:
                good++;
                lastJudgeColor = new Color(1f, 0.75f, 0.3f);
                // 빠르거나 늦은 만큼 옆으로 밀리고 흐릿하게 찍힌다
                AddMark(currentPaper, new Color(0.9f, 0.5f, 0.5f), Mathf.Sign((float)delta) * 0.025f, 0.85f);
                break;
            default:
                miss++;
                lastJudgeColor = new Color(0.6f, 0.7f, 1f);
                break;
        }
        lastJudgeText = judge + timing;

        // 찍은(또는 놓친) 서류는 왼쪽 서류함으로, 새 서류가 인턴 쪽에서 들어온다
        var done = currentPaper;
        Vector3 pileTop = paperExit + Vector3.up * (pile.Count * 0.002f);
        pile.Add(done);
        StartCoroutine(Slide(done, pileTop, 0.12f));
        if (pile.Count > 40)
        {
            Destroy(pile[0].gameObject);
            pile.RemoveAt(0);
        }

        currentPaper = SpawnPaper(paperEnter);
        StartCoroutine(Slide(currentPaper, stampPoint, 0.12f));
    }

    void ScheduleGuideTicks()
    {
        // 다음 찍을 박들을 0.3초 앞서 예약해서 정확한 타이밍에 울린다
        double now = conductor.SongTime;
        while (nextTick < noteTimes.Count && noteTimes[nextTick] < now + 0.3)
        {
            if (guideTick && noteTimes[nextTick] > now)
            {
                var src = tickSources[tickSourceIndex];
                tickSourceIndex = (tickSourceIndex + 1) % tickSources.Length;
                src.clip = guideTickClip;
                src.PlayScheduled(conductor.DspSongStart + noteTimes[nextTick]);
            }
            nextTick++;
        }
    }

    void AnimateStamp()
    {
        Vector3 up = stampPoint + Vector3.up * stampRestHeight;
        Vector3 down = stampPoint + Vector3.up * 0.03f;
        if (stampPressTimer > 0f)
        {
            stampPressTimer -= Time.deltaTime;
            stamp.position = down;
        }
        else
        {
            stamp.position = Vector3.Lerp(stamp.position, up, Time.deltaTime * 18f);
        }
    }

    IEnumerator Slide(Transform t, Vector3 to, float duration)
    {
        Vector3 from = t.position;
        for (float e = 0f; e < duration; e += Time.deltaTime)
        {
            if (t == null) yield break;
            t.position = Vector3.Lerp(from, to, Mathf.SmoothStep(0f, 1f, e / duration));
            yield return null;
        }
        if (t != null) t.position = to;
    }

    Transform SpawnPaper(Vector3 pos)
    {
        GameObject paper;
        if (paperPrefab != null)
        {
            paper = Instantiate(paperPrefab, pos, Quaternion.identity);
        }
        else
        {
            paper = new GameObject("Paper");
            paper.transform.position = pos;
            var sheet = Primitive(PrimitiveType.Cube, "Sheet", paper.transform, Vector3.zero,
                new Vector3(0.21f, 0.002f, 0.297f), new Color(0.96f, 0.95f, 0.9f));
            sheet.transform.localPosition = Vector3.zero;
        }
        return paper.transform;
    }

    void AddMark(Transform paper, Color color, float offsetX, float size)
    {
        var mark = Primitive(PrimitiveType.Cylinder, "StampMark", paper, Vector3.zero,
            new Vector3(0.05f * size, 0.0005f, 0.05f * size), color);
        mark.transform.localPosition = new Vector3(offsetX, 0.0015f, 0f);
    }

    // 씬 배치 에셋이 들어오기 전까지 쓰는 임시 책상과 도장
    Transform CreatePlaceholderScene()
    {
        var root = new GameObject("StampPrototype_Placeholder").transform;
        Primitive(PrimitiveType.Cube, "Desk", root, new Vector3(0f, -0.04f, 0f),
            new Vector3(2.4f, 0.08f, 1.0f), new Color(0.55f, 0.38f, 0.25f));
        Primitive(PrimitiveType.Cube, "DoneTray", root, new Vector3(-0.52f, 0.005f, 0f),
            new Vector3(0.26f, 0.01f, 0.34f), new Color(0.81f, 0.78f, 0.72f));

        var stampRoot = new GameObject("Stamp").transform;
        stampRoot.SetParent(root);
        stampRoot.position = stampPoint + Vector3.up * stampRestHeight;
        var body = Primitive(PrimitiveType.Cylinder, "Stamp_Body", stampRoot, Vector3.zero,
            new Vector3(0.05f, 0.03f, 0.05f), new Color(0.42f, 0.25f, 0.15f));
        body.transform.localPosition = Vector3.zero;
        var handle = Primitive(PrimitiveType.Sphere, "Stamp_Handle", stampRoot, Vector3.zero,
            Vector3.one * 0.04f, new Color(0.3f, 0.18f, 0.1f));
        handle.transform.localPosition = new Vector3(0f, 0.05f, 0f);
        return stampRoot;
    }

    static GameObject Primitive(PrimitiveType type, string name, Transform parent, Vector3 pos, Vector3 scale, Color color)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.position = pos;
        go.transform.localScale = scale;

        var shader = Shader.Find("Universal Render Pipeline/Lit");
        var mat = new Material(shader != null ? shader : Shader.Find("Standard"));
        mat.SetColor("_BaseColor", color);
        mat.color = color;
        go.GetComponent<Renderer>().material = mat;
        return go;
    }

    // 효과음이 아직 없어서 코드로 만든 임시 소리
    static AudioClip MakeClip(string name, float freq, float duration, float noise)
    {
        int rate = AudioSettings.outputSampleRate;
        int length = Mathf.Max(1, (int)(rate * duration));
        var data = new float[length];
        var rng = new System.Random(7);
        for (int i = 0; i < length; i++)
        {
            float t = (float)i / rate;
            float env = Mathf.Pow(1f - (float)i / length, 3f);
            float tone = Mathf.Sin(2f * Mathf.PI * freq * t);
            float hiss = (float)(rng.NextDouble() * 2.0 - 1.0);
            data[i] = (tone * (1f - noise) + hiss * noise) * env * 0.8f;
        }
        var clip = AudioClip.Create(name, length, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    void ReadInput(out bool press, out int chartKey, out bool restart, out bool toggleTick, out int offsetStep)
    {
        press = false; chartKey = -1; restart = false; toggleTick = false; offsetStep = 0;
#if ENABLE_INPUT_SYSTEM
        var kb = Keyboard.current;
        var mouse = Mouse.current;
        if (mouse != null && mouse.leftButton.wasPressedThisFrame) press = true;
        if (kb == null) return;
        if (kb.spaceKey.wasPressedThisFrame) press = true;
        if (kb.digit1Key.wasPressedThisFrame) chartKey = 0;
        if (kb.digit2Key.wasPressedThisFrame) chartKey = 1;
        if (kb.digit3Key.wasPressedThisFrame) chartKey = 2;
        if (kb.digit4Key.wasPressedThisFrame) chartKey = 3;
        restart = kb.rKey.wasPressedThisFrame;
        toggleTick = kb.gKey.wasPressedThisFrame;
        if (kb.leftBracketKey.wasPressedThisFrame) offsetStep = -1;
        if (kb.rightBracketKey.wasPressedThisFrame) offsetStep = 1;
#else
        press = Input.GetKeyDown(KeyCode.Space) || Input.GetMouseButtonDown(0);
        if (Input.GetKeyDown(KeyCode.Alpha1)) chartKey = 0;
        if (Input.GetKeyDown(KeyCode.Alpha2)) chartKey = 1;
        if (Input.GetKeyDown(KeyCode.Alpha3)) chartKey = 2;
        if (Input.GetKeyDown(KeyCode.Alpha4)) chartKey = 3;
        restart = Input.GetKeyDown(KeyCode.R);
        toggleTick = Input.GetKeyDown(KeyCode.G);
        if (Input.GetKeyDown(KeyCode.LeftBracket)) offsetStep = -1;
        if (Input.GetKeyDown(KeyCode.RightBracket)) offsetStep = 1;
#endif
    }

    // 비교용 임시 화면 표시(실제 게임에서는 UI 최소화 예정)
    void OnGUI()
    {
        if (hudStyle == null)
        {
            hudStyle = new GUIStyle(GUI.skin.label) { fontSize = 22 };
            hudStyle.normal.textColor = Color.white;
        }

        var chart = charts[chartIndex];
        string avg = offsetCount > 0 ? $"{offsetSum / offsetCount * 1000:+0;-0;0}ms" : "-";
        string text =
            $"채보 [{chartIndex + 1}] {chart.chartName}  패턴 \"{chart.pattern}\"  노트 {noteTimes.Count}개\n" +
            $"Perfect {perfect}   Good {good}   Miss {miss}   헛도장 {emptyPress}\n" +
            $"평균 오차 {avg}   입력 보정 {inputOffset * 1000:0}ms   가이드 틱 {(guideTick ? "켜짐" : "꺼짐")}\n" +
            "스페이스/클릭 도장 · 1~4 채보 · R 다시 · G 틱 · [ ] 보정";
        GUI.Label(new Rect(20, 20, 1200, 140), text, hudStyle);

        var judgeStyle = new GUIStyle(hudStyle) { fontSize = 40 };
        judgeStyle.normal.textColor = lastJudgeColor;
        GUI.Label(new Rect(20, 150, 800, 60), lastJudgeText, judgeStyle);

        if (conductor.SongFinished && nextNote >= noteTimes.Count)
        {
            int total = Mathf.Max(1, noteTimes.Count);
            float score = (perfect + good * 0.5f) / total * 100f;
            GUI.Label(new Rect(20, 220, 800, 60), $"끝! 점수 {score:0}점  (R로 다시)", judgeStyle);
        }
    }
}
