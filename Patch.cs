// Copyright (c) 2022 EchKode
// SPDX-License-Identifier: BSD-3-Clause

using HarmonyLib;

namespace EchKode.PBMods.EditRunAction
{
	[HarmonyPatch]
	static class Patch
	{
		[HarmonyPatch(typeof(CIViewCombatTimeline))]
		[HarmonyPatch("ConfigureActionPlanned", MethodType.Normal)]
		[HarmonyPostfix]
		static void Vct_ConfigureActionPlannedPostfix(CIHelperTimelineAction helper, int actionID)
		{
			Timeline.ConfigureActionPlanned(helper, actionID);
		}

		[HarmonyPatch(typeof(CIViewCombatTimeline))]
		[HarmonyPatch("OnActionSelected", MethodType.Normal)]
		[HarmonyPrefix]
		static void Vct_OnActionSelectedPrefix(int actionID)
		{
			Timeline.OnActionSelected(actionID);
		}

		[HarmonyPatch(typeof(CIViewCombatTimeline))]
		[HarmonyPatch("UpdateScrubbing", MethodType.Normal)]
		[HarmonyPostfix]
		static void Vct_UpdateScrubbingPostfix()
		{
			Timeline.UpdateScrubbingTimeFix();
		}
	}
}
