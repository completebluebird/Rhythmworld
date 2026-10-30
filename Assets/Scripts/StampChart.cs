using System;
using System.Collections.Generic;
using UnityEngine;

// 도장 찍을 박 목록(채보). 패턴 문자열을 바꿔 끼우면서 1박/2박/혼합을 비교한다.
// Project 창 우클릭 > Create > RhythmWorld > 도장 채보 로 만든다.
[CreateAssetMenu(menuName = "RhythmWorld/도장 채보", fileName = "StampChart")]
public class StampChart : ScriptableObject
{
    public string chartName = "새 채보";

    [Tooltip("첫 도장을 찍는 박 (13박 = 7.34초)")]
    public float startBeat = 13f;

    [Tooltip("마지막으로 볼 박. 0이면 곡 끝까지")]
    public float endBeat = 0f;

    [Tooltip("한 박을 몇 칸으로 나눌지. 1 = 정박만, 2 = 반박까지")]
    [Min(1)] public int stepsPerBeat = 1;

    [Tooltip("x = 도장, . = 쉼. 한 글자가 한 칸이고, 끝까지 반복된다. 예) x. = 2박마다, x.x.xxx. = 혼합")]
    public string pattern = "x.";

    [Tooltip("이 구간(박 번호 x 이상 y 미만)은 비운다. 예: 4박 쉬는 구간")]
    public List<Vector2> restRanges = new List<Vector2>();

    [Tooltip("패턴과 별개로 추가로 찍을 박")]
    public List<float> extraBeats = new List<float>();

    // 찍을 박 번호를 정렬해서 돌려준다. lastBeat는 곡의 마지막 박.
    public List<double> BuildBeats(int lastBeat)
    {
        var beats = new List<double>();
        double end = endBeat > 0 ? endBeat : lastBeat - 1;

        if (!string.IsNullOrEmpty(pattern))
        {
            double step = 1.0 / stepsPerBeat;
            for (int i = 0; ; i++)
            {
                double beat = startBeat + i * step;
                if (beat > end) break;
                if (pattern[i % pattern.Length] == 'x' && !IsResting(beat)) beats.Add(beat);
            }
        }

        foreach (var b in extraBeats)
        {
            if (!beats.Contains(b)) beats.Add(b);
        }

        beats.Sort();
        return beats;
    }

    bool IsResting(double beat)
    {
        foreach (var r in restRanges)
        {
            if (beat >= r.x && beat < r.y) return true;
        }
        return false;
    }

    // 채보 에셋이 없을 때 쓰는 기본 패턴
    public static StampChart CreatePreset(string name, string pattern, int stepsPerBeat = 1)
    {
        var chart = CreateInstance<StampChart>();
        chart.chartName = name;
        chart.pattern = pattern;
        chart.stepsPerBeat = Math.Max(1, stepsPerBeat);
        return chart;
    }
}
