using GuluPet.Behavior;

namespace GuluPet.Presentation;

internal static class BehaviorTriggerDescriptionFormatter
{
    private static readonly IReadOnlyDictionary<string, string>
        FriendlyDescriptions =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["trigger:active_tick"] =
                    "咕噜闲着的时候，偶尔会自己露出这一面",
                ["trigger:app:browser"] =
                    "妈咪安静看网页时，她偶尔会在旁边这样待着",
                ["trigger:app:communication"] =
                    "妈咪和别人聊天时，她偶尔会竖起耳朵听听",
                ["trigger:app:file_manager"] =
                    "妈咪整理东西时，她偶尔会凑过来看看",
                ["trigger:app:game"] =
                    "妈咪玩游戏时，她偶尔也想露一手",
                ["trigger:app:ide"] =
                    "妈咪专心敲代码时，她偶尔会在旁边陪着",
                ["trigger:app:media_player"] =
                    "妈咪看视频或听音乐时，她偶尔会这样放松",
                ["trigger:app:office"] =
                    "妈咪认真写东西时，她偶尔会在旁边守着",
                ["trigger:approach"] =
                    "妈咪把鼠标轻轻靠近她时",
                ["trigger:circle_pointer"] =
                    "妈咪用鼠标在她身边绕圈时",
                ["trigger:click"] =
                    "妈咪点她一下时",
                ["trigger:drag_release"] =
                    "妈咪把她抱到别处再轻轻放下时",
                ["trigger:feed"] =
                    "妈咪给她猫条时",
                ["trigger:hover"] =
                    "妈咪把鼠标停在她身边一会儿时",
                ["trigger:long_press"] =
                    "妈咪轻轻按住她一会儿时",
                ["trigger:pet_end"] =
                    "妈咪摸完她，刚把手收回去时",
                ["trigger:petting"] =
                    "妈咪来回摸摸她时",
                ["trigger:rapid_pointer"] =
                    "妈咪用鼠标飞快逗她时",
                ["trigger:repeat_click"] =
                    "妈咪连续点她好几下时",
                ["trigger:water"] =
                    "妈咪给她添水时",
                ["trigger:time:evening"] =
                    "傍晚陪着妈咪时，她偶尔会这样",
                ["trigger:time:holiday"] =
                    "假日陪着妈咪时，她偶尔会这样",
                ["trigger:time:late"] =
                    "夜深了还陪着妈咪时，她偶尔会这样",
                ["trigger:time:morning"] =
                    "清晨刚醒来时，她偶尔会这样",
                ["trigger:time:noon"] =
                    "午后有点犯困时，她偶尔会这样",
                ["trigger:time:weekend"] =
                    "周末和妈咪待在一起时，她偶尔会这样",
                ["trigger:user:away"] =
                    "妈咪离开座位一阵子后",
                ["trigger:user:busy"] =
                    "妈咪忙得停不下来时",
                ["trigger:user:idle"] =
                    "妈咪安静下来一会儿时",
                ["trigger:user:resume"] =
                    "妈咪回到电脑前时",
                ["trigger:user:return"] =
                    "妈咪离开一阵子又回来时",
                ["trigger:user:work25"] =
                    "妈咪专心忙了很久时",
                ["trigger:user:work50"] =
                    "妈咪忙了很久，还没停下来休息时",
                ["trigger:wake"] =
                    "平时她把这一面藏着，只有妈咪点来看看时才肯露一下",
                ["trigger:weather:clear"] =
                    "晴天的光落进来时，她偶尔会这样",
                ["trigger:weather:cloudy"] =
                    "窗外阴阴的时，她偶尔会这样",
                ["trigger:weather:cold"] =
                    "天气冷起来时，她偶尔会这样",
                ["trigger:weather:fog"] =
                    "窗外雾蒙蒙时，她偶尔会这样",
                ["trigger:weather:hot"] =
                    "天气热起来时，她偶尔会这样",
                ["trigger:weather:rain"] =
                    "听见雨声时，她偶尔会这样",
                ["trigger:weather:storm"] =
                    "雷声靠近时，她偶尔会这样",
            };

    public static string Describe(BehaviorDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        string[] descriptions = definition.Tags
            .Where(static tag =>
                tag.StartsWith("trigger:", StringComparison.Ordinal))
            .Select(static tag =>
                FriendlyDescriptions.TryGetValue(tag, out string? description)
                    ? description
                    : "咕噜什么时候愿意这样，暂时还是个秘密")
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (descriptions.Length == 0)
        {
            return DescribeFallback(definition);
        }

        return descriptions.Length == 1
            ? descriptions[0]
            : $"这些时候，她偶尔会这样：{string.Join("；", descriptions)}";
    }

    private static string DescribeFallback(BehaviorDefinition definition)
    {
        if (definition.IsStateFallback)
        {
            return "什么都不做时，她喜欢这样安静陪在妈咪身边";
        }

        if (definition.Tags.Contains(
                "fallback:interaction",
                StringComparer.Ordinal))
        {
            return "妈咪来找她时，她偶尔只肯给一点小回应";
        }

        return "平时她把这一面藏得很好，点一下才肯给妈咪看";
    }
}
