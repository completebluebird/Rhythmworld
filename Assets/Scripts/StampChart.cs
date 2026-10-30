using System.Collections.Generic;
using UnityEngine;

// 서류 순서표(채보). 서류 한 장 = 4박: 1박 인턴이 건넴, 2박 도장, 3박 서명, 4박 인턴이 회수.
// Project 창 우클릭 > Create > RhythmWorld > 도장 채보 로 만든다.
[CreateAssetMenu(menuName = "RhythmWorld/도장 채보", fileName = "StampChart")]
public class StampChart : ScriptableObject
{
    public struct PaperSlot
    {
        public double beat;   // 서류의 1박
        public char type;     // 'n' 일반, 's' 시크릿, 't' 함정
    }

    public string chartName = "새 채보";

    [Tooltip("첫 서류의 1박이 오는 박 (14 = 음악 진입(10박) 다음 첫 강박)")]
    public float firstPaperBeat = 14f;

    [Tooltip("마지막으로 볼 박. 0이면 곡 끝까지")]
    public float endBeat = 0f;

    [Tooltip("한 글자 = 서류 한 장(4박). n = 일반, s = 시크릿, t = 함정, . = 4박 쉼. 끝까지 반복된다. 예) nnst")]
    public string papers = "n";

    [Tooltip("이 구간(박 번호 x 이상 y 미만)에 1박이 걸리는 서류는 비운다")]
    public List<Vector2> restRanges = new List<Vector2>();

    // 서류 목록을 순서대로 돌려준다. lastBeat는 곡의 마지막 박. 4박째까지 곡 안에 들어오는 서류만 넣는다.
    public List<PaperSlot> BuildPapers(int lastBeat)
    {
        var slots = new List<PaperSlot>();
        if (string.IsNullOrEmpty(papers)) return slots;

        double end = endBeat > 0 ? endBeat : lastBeat;
        for (int i = 0; ; i++)
        {
            double beat = firstPaperBeat + i * 4.0;
            if (beat + 3.0 > end) break;
            char c = char.ToLowerInvariant(papers[i % papers.Length]);
            if ((c == 'n' || c == 's' || c == 't') && !IsResting(beat))
                slots.Add(new PaperSlot { beat = beat, type = c });
        }
        return slots;
    }

    bool IsResting(double beat)
    {
        foreach (var r in restRanges)
        {
            if (beat >= r.x && beat < r.y) return true;
        }
        return false;
    }

    // 채보 에셋이 없을 때 쓰는 기본 순서
    public static StampChart CreatePreset(string name, string papers)
    {
        var chart = CreateInstance<StampChart>();
        chart.chartName = name;
        chart.papers = papers;
        return chart;
    }
}
