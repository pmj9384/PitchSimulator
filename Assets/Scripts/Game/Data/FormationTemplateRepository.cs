using System;
using System.Collections.Generic;
using Game.Core.Data;
using UnityEngine;

// FormationTemplates 접근층. 팀 생성기가 통째로 받으므로 목록만 준다
public static class FormationTemplateRepository
{
    private const string ResourcePath = "Tables/FormationTemplates";
    private static List<FormationTemplate> ordered;

    public static IReadOnlyList<FormationTemplate> All
    {
        get
        {
            if (ordered != null) { return ordered; }

            TextAsset csv = Resources.Load<TextAsset>(ResourcePath);
            if (csv == null)
            {
                throw new InvalidOperationException($"[FormationTemplateRepository] {ResourcePath} 없음");
            }
            ordered = FormationTemplateParser.Parse(csv.text);
            return ordered;
        }
    }
}
