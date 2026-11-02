using System.Collections;
using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

// 도장찍기 프로토타입: 서류 한 장 = 4박.
//   1박 예고음 + 인턴이 서류를 건넴 / 2박 왼손 도장 / 3박 오른손 서명 / 4박 인턴이 서류를 가져감
// 서류 종류: 일반 = 2박 도장, 3박 서명 / 시크릿 = 2박·2.5박 도장 두 번, 3박 서명 / 함정 = 도장 금지, 3박 서명
// 조작: F·스페이스·마우스 왼쪽 = 도장, J·마우스 오른쪽 = 서명, 1~5 = 채보 바꾸고 처음부터, R = 처음부터,
//       G = 가이드 틱 켜기/끄기, [ ] = 입력 보정 -10ms/+10ms
// 도장·펜·서류는 비워 두면 임시 도형을 만든다(서류 종류는 지금은 색으로만 구분).
public class StampController : MonoBehaviour
{
    public Conductor conductor;

    [Header("채보 (비워 두면 기본 4개 사용)")]
    public List<StampChart> charts = new List<StampChart>();
    public int chartIndex = 0;

    [Header("판정 폭 (초, 임시값)")]
    public float perfectWindow = 0.045f;
    public float goodWindow = 0.10f;

    [Tooltip("입력 보정(초). 결과의 평균 오차를 보고 맞춘다. +면 늦게 누르는 버릇을 봐준다")]
    public float inputOffset = 0f;

    [Header("오브젝트 (비우면 임시 도형 생성)")]
    public Transform stamp;
    public Transform pen;
    public Transform intern;          // 1박·4박에 서류 쪽으로 손을 뻗는다
    public GameObject paperPrefab;    // 서류 한 장. 종류는 색과 띠·사진으로 구분
    public Vector3 paperPoint = new Vector3(0f, 0f, 0.02f);
    public Vector3 internPoint = new Vector3(0f, 0.001f, 0.45f);   // 인턴 쪽(화면 위)
    public Vector3 internReach = new Vector3(0f, 0f, -0.12f);      // 손 뻗을 때 인턴이 움직이는 거리
    public bool setupCamera = true;

    [Header("소리 (비우면 도장·서명·틱은 임시 소리, 나머지는 무음)")]
    public AudioClip normalCue;     // normal_start
    public AudioClip secretCue;     // secret_start
    public AudioClip trapCue;       // Trap_start
    public AudioClip stampClip;     // stamp_sound
    public AudioClip signClip;      // sign_sound
    public AudioClip successClip;   // success: 서류를 문제없이 처리함
    public AudioClip failClip;      // fail: 놓치거나 함정에 도장을 찍음
    public AudioClip guideTickClip;
    public bool guideTick = true;   // 눌러야 할 박마다 '틱' 소리로 정답 타이밍을 들려줌(비교용)

    enum Judge { Perfect, Good, Miss, Bad }

    class Paper
    {
        public int type;      // 0 일반, 1 시크릿, 2 함정
        public double time;   // 1박 시간(초)
        public Transform obj;
        public float top;     // 서류 윗면 높이(자국을 올릴 곳)
        public bool failed;
        public int stamps;
    }

    class Note
    {
        public double time;
        public Paper paper;
    }

    static readonly string[] TypeNames = { "일반", "시크릿", "함정" };

    readonly List<Paper> papers = new List<Paper>();
    readonly List<Note> stampNotes = new List<Note>();
    readonly List<Note> signNotes = new List<Note>();
    readonly List<KeyValuePair<double, AudioClip>> scheduled = new List<KeyValuePair<double, AudioClip>>();
    int nextStamp, nextSign, nextSpawn, nextLeave, nextScheduled;

    int perfect, good, miss, bad, emptyPress;
    readonly int[] typeTotal = new int[3];
    readonly int[] typeCleared = new int[3];
    double offsetSum;
    int offsetCount;
    string lastJudgeText = "";
    Color lastJudgeColor = Color.white;

    Vector3 stampRest, penRest, internRest;
    float stampPressTimer, penPressTimer, internReachTimer;

    AudioSource sfxSource;
    AudioSource[] scheduledSources;
    int scheduledSourceIndex;

    GUIStyle hudStyle;

    void Awake()
    {
        if (conductor == null) conductor = FindAnyObjectByType<Conductor>();
        conductor.playOnStart = false;

        if (charts.Count == 0)
        {
            charts.Add(StampChart.CreatePreset("일반만", "n"));
            charts.Add(StampChart.CreatePreset("일반+시크릿", "nnns"));
            charts.Add(StampChart.CreatePreset("일반+함정", "nnnt"));
            charts.Add(StampChart.CreatePreset("혼합", "nnsnnt.ntns"));
        }
        chartIndex = Mathf.Clamp(chartIndex, 0, charts.Count - 1);

        sfxSource = gameObject.AddComponent<AudioSource>();
        sfxSource.playOnAwake = false;
        scheduledSources = new AudioSource[8];
        for (int i = 0; i < scheduledSources.Length; i++)
        {
            scheduledSources[i] = gameObject.AddComponent<AudioSource>();
            scheduledSources[i].playOnAwake = false;
        }
        if (stampClip == null) stampClip = MakeClip("StampThump", 110f, 0.09f, 0.45f);
        if (signClip == null) signClip = MakeClip("PenScratch", 900f, 0.08f, 0.9f);
        if (guideTickClip == null) guideTickClip = MakeClip("GuideTick", 1600f, 0.03f, 0f);

        if (stamp == null || pen == null || intern == null) CreatePlaceholderScene();
        stampRest = stamp.position;
        penRest = pen.position;
        internRest = intern.position;
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
        foreach (var p in papers) if (p.obj != null) Destroy(p.obj.gameObject);
        foreach (var src in scheduledSources) src.Stop();

        conductor.StartSong();

        papers.Clear();
        stampNotes.Clear();
        signNotes.Clear();
        scheduled.Clear();
        System.Array.Clear(typeTotal, 0, 3);
        System.Array.Clear(typeCleared, 0, 3);

        double spb = conductor.SecPerBeat;
        foreach (var slot in charts[chartIndex].BuildPapers(conductor.LastBeat))
        {
            int type = slot.type == 's' ? 1 : slot.type == 't' ? 2 : 0;
            var p = new Paper { type = type, time = conductor.BeatToTime(slot.beat) };
            papers.Add(p);
            typeTotal[type]++;

            scheduled.Add(new KeyValuePair<double, AudioClip>(p.time, type == 1 ? secretCue : type == 2 ? trapCue : normalCue));
            if (type != 2) stampNotes.Add(new Note { time = p.time + spb, paper = p });
            if (type == 1) stampNotes.Add(new Note { time = p.time + spb * 1.5, paper = p });
            signNotes.Add(new Note { time = p.time + spb * 2, paper = p });
        }
        // 가이드 틱은 null 클립으로 표시해 두고 울릴 때 켜져 있는지 본다
        foreach (var n in stampNotes) scheduled.Add(new KeyValuePair<double, AudioClip>(n.time, null));
        foreach (var n in signNotes) scheduled.Add(new KeyValuePair<double, AudioClip>(n.time, null));
        scheduled.Sort((a, b) => a.Key.CompareTo(b.Key));

        nextStamp = nextSign = nextSpawn = nextLeave = nextScheduled = 0;
        perfect = good = miss = bad = emptyPress = 0;
        offsetSum = 0;
        offsetCount = 0;
        lastJudgeText = "";
    }

    void Update()
    {
        ReadInput(out bool stampPress, out bool signPress, out int chartKey, out bool restart, out bool toggleTick, out int offsetStep);

        if (chartKey >= 0 && chartKey < charts.Count)
        {
            chartIndex = chartKey;
            Restart();
            return;
        }
        if (restart) { Restart(); return; }
        if (toggleTick) guideTick = !guideTick;
        if (offsetStep != 0) inputOffset += offsetStep * 0.01f;

        double now = conductor.SongTimeNow - inputOffset;
        if (stampPress) OnStamp(now);
        if (signPress) OnSign(now);

        // 판정 폭을 지나도록 안 누른 입력은 Miss
        nextStamp = SweepMisses(stampNotes, nextStamp, now);
        nextSign = SweepMisses(signNotes, nextSign, now);

        UpdatePapers(conductor.SongTimeNow);
        ScheduleSounds();
        AnimateTool(stamp, ref stampPressTimer, stampRest, paperPoint + new Vector3(-0.05f, 0.03f, -0.03f));
        AnimateTool(pen, ref penPressTimer, penRest, paperPoint + new Vector3(0.07f, 0.012f, -0.1f));

        internReachTimer -= Time.deltaTime;
        var internTarget = internReachTimer > 0f ? internRest + internReach : internRest;
        intern.position = Vector3.Lerp(intern.position, internTarget, Time.deltaTime * 14f);
    }

    void OnStamp(double t)
    {
        stampPressTimer = 0.08f;
        sfxSource.PlayOneShot(stampClip);

        if (TryHit(stampNotes, ref nextStamp, t, true)) return;

        // 책상 위 서류가 함정이면 도장을 찍은 것 자체가 Bad
        var onDesk = nextLeave < nextSpawn ? papers[nextLeave] : null;
        if (onDesk != null && onDesk.type == 2)
        {
            bad++;
            onDesk.failed = true;
            AddStampMark(onDesk, new Color(0.25f, 0.25f, 0.25f), -0.01f, 25f, 0.6f);   // 함정에 찍은 도장은 비뚤어지게
            ShowJudge("Bad (함정에 도장)", new Color(0.7f, 0.4f, 1f));
            return;
        }
        emptyPress++;
        ShowJudge("헛도장", Color.gray);
    }

    void OnSign(double t)
    {
        penPressTimer = 0.08f;
        sfxSource.PlayOneShot(signClip);

        if (TryHit(signNotes, ref nextSign, t, false)) return;
        emptyPress++;
        ShowJudge("헛서명", Color.gray);
    }

    bool TryHit(List<Note> notes, ref int index, double t, bool isStamp)
    {
        if (index >= notes.Count) return false;
        var note = notes[index];
        double delta = t - note.time;
        double abs = System.Math.Abs(delta);
        if (abs > goodWindow) return false;

        index++;
        offsetSum += delta;
        offsetCount++;
        bool isPerfect = abs <= perfectWindow;
        if (isPerfect) perfect++; else good++;

        // Good은 빠르거나 늦은 만큼 옆으로 밀리고 살짝 기울고 흐릿하게 남는다
        float side = Mathf.Sign((float)delta);
        float shift = isPerfect ? 0f : side * 0.02f;
        float tilt = isPerfect ? 0f : side * 10f;
        if (isStamp) AddStampMark(note.paper, isPerfect ? new Color(0.85f, 0.05f, 0.05f) : new Color(0.95f, 0.72f, 0.72f), shift, tilt, 1f);
        else AddSignMark(note.paper, isPerfect ? new Color(0.1f, 0.1f, 0.35f) : new Color(0.5f, 0.5f, 0.62f), shift, tilt);

        int ms = (int)System.Math.Round(delta * 1000);
        string timing = ms > 0 ? $" (+{ms}ms 늦음)" : ms < 0 ? $" ({ms}ms 빠름)" : " (0ms)";
        ShowJudge((isPerfect ? Judge.Perfect : Judge.Good) + timing,
            isPerfect ? new Color(1f, 0.35f, 0.3f) : new Color(1f, 0.75f, 0.3f));
        return true;
    }

    int SweepMisses(List<Note> notes, int index, double t)
    {
        while (index < notes.Count && t > notes[index].time + goodWindow)
        {
            miss++;
            notes[index].paper.failed = true;
            ShowJudge(Judge.Miss.ToString(), new Color(0.6f, 0.7f, 1f));
            index++;
        }
        return index;
    }

    // 1박에 서류가 인턴 쪽에서 들어오고, 4박에 결과음과 함께 인턴 쪽으로 돌아가 사라진다
    void UpdatePapers(double t)
    {
        double spb = conductor.SecPerBeat;
        while (nextSpawn < papers.Count && t >= papers[nextSpawn].time)
        {
            var p = papers[nextSpawn++];
            SpawnPaper(p);
            internReachTimer = (float)spb * 0.4f;
            StartCoroutine(Slide(p.obj, paperPoint, (float)spb * 0.3f, false));
        }
        while (nextLeave < nextSpawn && t >= papers[nextLeave].time + spb * 3)
        {
            var p = papers[nextLeave++];
            internReachTimer = (float)spb * 0.4f;
            if (!p.failed) typeCleared[p.type]++;
            var clip = p.failed ? failClip : successClip;
            if (clip != null) sfxSource.PlayOneShot(clip);
            StartCoroutine(Slide(p.obj, internPoint, (float)spb * 0.3f, true));
        }
    }

    void ScheduleSounds()
    {
        // 0.3초 앞서 예약해서 정확한 박에 울린다
        double now = conductor.SongTime;
        while (nextScheduled < scheduled.Count && scheduled[nextScheduled].Key < now + 0.3)
        {
            var item = scheduled[nextScheduled++];
            var clip = item.Value != null ? item.Value : guideTick ? guideTickClip : null;
            if (clip == null || item.Key <= now) continue;

            var src = scheduledSources[scheduledSourceIndex];
            scheduledSourceIndex = (scheduledSourceIndex + 1) % scheduledSources.Length;
            src.clip = clip;
            src.volume = item.Value != null ? 1f : 0.5f;
            src.PlayScheduled(conductor.DspSongStart + item.Key);
        }
    }

    void ShowJudge(string text, Color color)
    {
        lastJudgeText = text;
        lastJudgeColor = color;
    }

    static void AnimateTool(Transform tool, ref float timer, Vector3 rest, Vector3 down)
    {
        if (timer > 0f)
        {
            timer -= Time.deltaTime;
            tool.position = down;
        }
        else
        {
            tool.position = Vector3.Lerp(tool.position, rest, Time.deltaTime * 18f);
        }
    }

    IEnumerator Slide(Transform t, Vector3 to, float duration, bool destroyAtEnd)
    {
        Vector3 from = t.position;
        for (float e = 0f; e < duration; e += Time.deltaTime)
        {
            if (t == null) yield break;
            t.position = Vector3.Lerp(from, to, Mathf.SmoothStep(0f, 1f, e / duration));
            yield return null;
        }
        if (t == null) yield break;
        t.position = to;
        if (destroyAtEnd) Destroy(t.gameObject);
    }

    // 지금은 색으로만 구분: 일반 = 흰 종이, 시크릿 = 주황 종이 + 빨간 띠, 함정 = 흰 종이 + 회색·노란 '사장 사진'
    void SpawnPaper(Paper p)
    {
        int type = p.type;
        var paper = new GameObject("Paper_" + TypeNames[type]).transform;
        paper.position = internPoint;
        var sheetColor = type == 1 ? new Color(1f, 0.8f, 0.55f) : Color.white;
        if (paperPrefab != null)
        {
            var sheet = Instantiate(paperPrefab, paper, false);
            sheet.transform.localPosition = Vector3.zero;
            sheet.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);   // 글씨가 내 쪽에서 바로 읽히게
            float maxY = 0f;
            foreach (var r in sheet.GetComponentsInChildren<Renderer>())
            {
                if (type == 1) r.material.SetColor("_BaseColor", sheetColor);
                maxY = Mathf.Max(maxY, r.bounds.max.y - paper.position.y);
            }
            p.top = maxY;
        }
        else
        {
            Primitive(PrimitiveType.Cube, "Sheet", paper, new Vector3(0.21f, 0.002f, 0.297f), sheetColor, Vector3.zero);
            p.top = 0.001f;
        }
        float y = p.top + 0.0005f;
        if (type == 1)
            Primitive(PrimitiveType.Cube, "SecretBand", paper, new Vector3(0.21f, 0.0005f, 0.035f), new Color(0.85f, 0.1f, 0.1f), new Vector3(0f, y, 0.12f));
        if (type == 2)
            Primitive(PrimitiveType.Cube, "BossPhoto", paper, new Vector3(0.07f, 0.0005f, 0.08f), new Color(0.75f, 0.7f, 0.4f), new Vector3(0.05f, y, 0.07f));
        p.obj = paper;
    }

    // 시크릿의 두 번째 도장은 첫 번째와 살짝 어긋나게 겹쳐 찍힌다(두 번 찍힌 자국이 보이도록)
    static void AddStampMark(Paper paper, Color color, float shiftX, float tiltDeg, float squash)
    {
        if (paper.obj == null) return;
        int n = paper.stamps++;
        var pos = new Vector3(-0.05f + shiftX + n * 0.01f, paper.top + 0.001f + n * 0.0002f, -0.03f + n * 0.012f);
        var mark = Primitive(PrimitiveType.Cylinder, "StampMark", paper.obj, new Vector3(0.05f, 0.0005f, 0.05f * squash), color, pos);
        mark.transform.localRotation = Quaternion.Euler(0f, tiltDeg, 0f);
    }

    static void AddSignMark(Paper paper, Color color, float shiftX, float tiltDeg)
    {
        if (paper.obj == null) return;
        var mark = Primitive(PrimitiveType.Cube, "SignMark", paper.obj, new Vector3(0.07f, 0.0005f, 0.008f), color, new Vector3(0.06f + shiftX, paper.top + 0.001f, -0.09f));
        mark.transform.localRotation = Quaternion.Euler(0f, tiltDeg, 0f);
    }

    // 씬에 없는 것만 임시 도형으로 만든다: 도장(왼쪽), 펜(오른쪽), 인턴(책상과 함께)
    void CreatePlaceholderScene()
    {
        var root = new GameObject("StampPrototype_Placeholder").transform;
        if (intern == null)
        {
            Primitive(PrimitiveType.Cube, "Desk", root, new Vector3(2.4f, 0.08f, 1.0f), new Color(0.55f, 0.38f, 0.25f), new Vector3(0f, -0.04f, 0f));
            intern = Primitive(PrimitiveType.Cube, "Intern", root, new Vector3(0.3f, 0.25f, 0.08f), new Color(0.35f, 0.5f, 0.7f), internPoint + new Vector3(0f, 0.12f, 0.12f)).transform;
        }

        if (stamp == null)
        {
            stamp = new GameObject("Stamp").transform;
            stamp.SetParent(root);
            stamp.position = paperPoint + new Vector3(-0.3f, 0.12f, 0f);
            Primitive(PrimitiveType.Cylinder, "Stamp_Body", stamp, new Vector3(0.05f, 0.03f, 0.05f), new Color(0.42f, 0.25f, 0.15f), Vector3.zero);
            Primitive(PrimitiveType.Sphere, "Stamp_Handle", stamp, Vector3.one * 0.04f, new Color(0.3f, 0.18f, 0.1f), new Vector3(0f, 0.05f, 0f));
        }
        if (pen == null)
        {
            pen = new GameObject("Pen").transform;
            pen.SetParent(root);
            pen.position = paperPoint + new Vector3(0.3f, 0.12f, 0f);
            var body = Primitive(PrimitiveType.Cylinder, "Pen_Body", pen, new Vector3(0.012f, 0.07f, 0.012f), new Color(0.1f, 0.1f, 0.12f), new Vector3(0f, 0.04f, 0f));
            body.transform.localRotation = Quaternion.Euler(0f, 0f, -20f);
        }
    }

    static GameObject Primitive(PrimitiveType type, string name, Transform parent, Vector3 scale, Color color, Vector3 localPos)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = scale;

        var shader = Shader.Find("Universal Render Pipeline/Lit");
        var mat = new Material(shader != null ? shader : Shader.Find("Standard"));
        mat.SetColor("_BaseColor", color);
        mat.color = color;
        go.GetComponent<Renderer>().material = mat;
        return go;
    }

    // 효과음이 없을 때 코드로 만드는 임시 소리
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

    void ReadInput(out bool stampPress, out bool signPress, out int chartKey, out bool restart, out bool toggleTick, out int offsetStep)
    {
        stampPress = false; signPress = false; chartKey = -1; restart = false; toggleTick = false; offsetStep = 0;
#if ENABLE_INPUT_SYSTEM
        var kb = Keyboard.current;
        var mouse = Mouse.current;
        if (mouse != null)
        {
            if (mouse.leftButton.wasPressedThisFrame) stampPress = true;
            if (mouse.rightButton.wasPressedThisFrame) signPress = true;
        }
        if (kb == null) return;
        if (kb.fKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame) stampPress = true;
        if (kb.jKey.wasPressedThisFrame) signPress = true;
        if (kb.digit1Key.wasPressedThisFrame) chartKey = 0;
        if (kb.digit2Key.wasPressedThisFrame) chartKey = 1;
        if (kb.digit3Key.wasPressedThisFrame) chartKey = 2;
        if (kb.digit4Key.wasPressedThisFrame) chartKey = 3;
        if (kb.digit5Key.wasPressedThisFrame) chartKey = 4;
        restart = kb.rKey.wasPressedThisFrame;
        toggleTick = kb.gKey.wasPressedThisFrame;
        if (kb.leftBracketKey.wasPressedThisFrame) offsetStep = -1;
        if (kb.rightBracketKey.wasPressedThisFrame) offsetStep = 1;
#else
        stampPress = Input.GetKeyDown(KeyCode.F) || Input.GetKeyDown(KeyCode.Space) || Input.GetMouseButtonDown(0);
        signPress = Input.GetKeyDown(KeyCode.J) || Input.GetMouseButtonDown(1);
        if (Input.GetKeyDown(KeyCode.Alpha1)) chartKey = 0;
        if (Input.GetKeyDown(KeyCode.Alpha2)) chartKey = 1;
        if (Input.GetKeyDown(KeyCode.Alpha3)) chartKey = 2;
        if (Input.GetKeyDown(KeyCode.Alpha4)) chartKey = 3;
        if (Input.GetKeyDown(KeyCode.Alpha5)) chartKey = 4;
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
            $"채보 [{chartIndex + 1}] {chart.chartName}  순서 \"{chart.papers}\"  서류 {papers.Count}장\n" +
            $"일반 {typeCleared[0]}/{typeTotal[0]}   시크릿 {typeCleared[1]}/{typeTotal[1]}   함정 {typeCleared[2]}/{typeTotal[2]}\n" +
            $"Perfect {perfect}   Good {good}   Miss {miss}   Bad {bad}   헛입력 {emptyPress}\n" +
            $"평균 오차 {avg}   입력 보정 {inputOffset * 1000:0}ms   가이드 틱 {(guideTick ? "켜짐" : "꺼짐")}\n" +
            "F/스페이스/왼클릭 도장 · J/오른클릭 서명 · 1~5 채보 · R 다시 · G 틱 · [ ] 보정";
        GUI.Label(new Rect(20, 20, 1400, 170), text, hudStyle);

        var judgeStyle = new GUIStyle(hudStyle) { fontSize = 40 };
        judgeStyle.normal.textColor = lastJudgeColor;
        GUI.Label(new Rect(20, 180, 800, 60), lastJudgeText, judgeStyle);

        if (conductor.SongFinished && nextLeave >= papers.Count)
        {
            int cleared = typeCleared[0] + typeCleared[1] + typeCleared[2];
            GUI.Label(new Rect(20, 250, 900, 60), $"끝! 처리한 서류 {cleared}/{papers.Count}장  (R로 다시)", judgeStyle);
        }
    }
}
