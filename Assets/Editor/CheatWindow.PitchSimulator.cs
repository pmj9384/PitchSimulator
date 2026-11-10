using Game.Core.League;
using UnityEditor;
using UnityEngine;

// PitchSimulator 전용 치트(10-19): 시즌을 헤드리스로 돌리거나 결과를 정해 넣어 승강전·시즌 끝 화면을 빨리 본다. 보고 경로는 인게임과 같다(SeasonSystem.ReportMyResultAsync)
public partial class CheatWindow
{
    private bool autoPlaying;

    partial void DrawProjectCheats()
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("경기 길이 (개발·녹화용)", EditorStyles.boldLabel);
        DevMatchLength.RealTime = EditorGUILayout.Toggle("인게임 실제 90분", DevMatchLength.RealTime);
        EditorGUILayout.LabelField("다음 경기부터 적용. 기본은 3분 압축(튜닝·대시보드는 실제 90분)");
        DrawSpeedButtons();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("시즌", EditorStyles.boldLabel);
        SeasonSystem season = GameDataManager.Instance.Season;
        EditorGUILayout.LabelField($"현재: {season.NextMatchLine()}  ·  보고 중 {season.IsReporting}");
        EditorGUILayout.LabelField(StandingLine(season));

        bool locked = autoPlaying || season.IsReporting || season.IsOver;
        using (new EditorGUI.DisabledScope(locked))
        {
            if (GUILayout.Button("다음 경기 자동 진행 (헤드리스)")) { AutoPlay(1, null); }
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("승리로 (1-0)")) { AutoPlay(1, new Vector2Int(1, 0)); }
            if (GUILayout.Button("무승부로 (0-0)")) { AutoPlay(1, new Vector2Int(0, 0)); }
            if (GUILayout.Button("패배로 (0-1)")) { AutoPlay(1, new Vector2Int(0, 1)); }
            EditorGUILayout.EndHorizontal();
            if (GUILayout.Button("정규 시즌 끝까지 자동 진행")) { AutoPlay(season.Tier.Matches, null); }
        }
        EditorGUILayout.HelpBox("승강전은 2·3위(승격 PO)와 최하위 바로 위(강등 PO)만 친다. 1위는 바로 승격, 최하위는 바로 강등. 앞은 승으로 쌓고 끝에 패·무로 2·3위에 맞춘다", MessageType.None);
    }

    // "현재 3위 · 승점 12 · 1위와 −3 · 4위와 +2": 위아래 승점 차를 보고 승·무·패를 고른다
    private static string StandingLine(SeasonSystem season)
    {
        LeagueTable table = season.Table();
        int rank = table.RankOf(SeasonState.MyTeamId);
        int points = table[rank].Points;
        string up = rank > 0 ? $" · {rank}위와 {points - table[rank - 1].Points:+0;-0;0}" : string.Empty;
        string down = rank < table.Count - 1 ? $" · {rank + 2}위와 {points - table[rank + 1].Points:+0;-0;0}" : string.Empty;
        return $"현재 {rank + 1}위 / {table.Count} · 승점 {points}{up}{down}";
    }

    // 한 경기씩 보고가 끝나길 기다린다. 승강전 단계에 들어가면 IsRegularSeasonOver라 정규 횟수 루프가 거기서 멈춘다. score = 정한 점수(내 골, 상대 골), null이면 헤드리스 시뮬
    private async void AutoPlay(int matches, Vector2Int? score)
    {
        autoPlaying = true;
        try
        {
            SeasonSystem season = GameDataManager.Instance.Season;
            for (int i = 0; i < matches; i++)
            {
                if (season.IsOver) { break; }
                if (season.IsRegularSeasonOver && i > 0) { break; }
                if (score.HasValue) { await season.ReportFixedResultAsync(score.Value.x, score.Value.y); }
                else { await season.AutoPlayCurrentMatchAsync(); }
                Repaint();
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[Cheat] 자동 진행 실패: {e}");
        }
        finally
        {
            autoPlaying = false;
            Repaint();
        }
    }

    // 녹화용 배속(11-10): 실제 90분 경기에서 장면을 기다리는 시간을 줄인다. 화면 속도(Time.timeScale)만 바꾸고 시뮬은 고정 스텝이라 결과는 같다.
    // 일시정지 복귀(GameManager)가 1배로 되돌리므로 다시 누른다
    private static readonly float[] Speeds = { 1f, 2f, 4f, 8f };

    private void DrawSpeedButtons()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField($"배속 (지금 {Time.timeScale:0.#}×)", GUILayout.Width(110));
            for (int i = 0; i < Speeds.Length; i++)
            {
                if (GUILayout.Button($"{Speeds[i]:0}×")) { Time.timeScale = Speeds[i]; }
            }
        }
    }
}
