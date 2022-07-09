using System.Collections.Generic;

using HarmonyLib;
using UnityEngine;

namespace EchKode.PBMods.EditRunAction
{
	using PlannedActionMap = Dictionary<int, CIHelperTimelineAction>;

	static class Timeline
	{
		// This is the sentinel value I've seen in the code to indicate a null entity ID.
		private const int unknownID = -99;

		// Estimated time length of the pointy part of the painted action sprite.
		internal const float paintedActionTipDuration = 5f / 32;

		private static System.Action<object> onActionDrag;
		private static System.Action<object> onActionDragEnd;

		internal static PlannedActionMap helpersActionsPlanned;

		private static int selectedActionID;

		internal static float scrubTimeTargetMinimum;
		internal static float scrubTimeTargetMaximum;

		internal static void ResetScrubRangeLimit()
		{
			scrubTimeTargetMinimum = scrubTimeTargetMaximum = 0f;
		}

		internal static void ConfigureActionPlanned(CIHelperTimelineAction helper, int actionID)
		{
			ActionEntity actionEntity = PhantomBrigade.IDUtility.GetActionEntity(actionID);
			if (actionEntity.isMovementExtrapolated || !actionEntity.hasStartTime || !actionEntity.hasDuration)
			{
				return;
			}

			if (onActionDrag == null)
			{
				Initialize();
			}

			var button = helper.button;
			button.callbackOnDragHorizontal = new UICallback(new System.Action<object>(OnActionDrag), null);
			button.callbackOnDragHorizontal.argumentInt = actionID;
			button.callbackOnDragHorizontal.argumentObject = helper.button.callbackOnDragHorizontal;
			button.callbackOnDragEnd = new UICallback(new System.Action<object>(OnActionDragEnd), null);
			button.callbackOnDragEnd.argumentInt = actionID;
			button.callbackOnDragEnd.argumentObject = helper.button.callbackOnDragEnd;
		}

		private static void Initialize()
		{
			var ins = CIViewCombatTimeline.ins;
			onActionDrag = (System.Action<object>)AccessTools.Method(typeof(CIViewCombatTimeline), "OnActionDrag")
				.CreateDelegate(typeof(System.Action<object>), ins);
			onActionDragEnd = (System.Action<object>)AccessTools.Method(typeof(CIViewCombatTimeline), "OnActionDragEnd")
				.CreateDelegate(typeof(System.Action<object>), ins);
			helpersActionsPlanned = (PlannedActionMap)AccessTools.Field(typeof(CIViewCombatTimeline), nameof(helpersActionsPlanned)).GetValue(ins);

			selectedActionID = unknownID;
			Movement.Initialize();
		}

		private static void OnActionDrag(object arg)
		{
			if (!PhantomBrigade.CombatUIUtility.IsCombatUISafe() || !(arg is UICallback))
			{
				return;
			}

			var uiCallback = (UICallback)arg;
			var actionID = uiCallback.argumentInt;
			if (!helpersActionsPlanned.ContainsKey(actionID))
			{
				selectedActionID = unknownID;
				return;
			}

			if (selectedActionID != actionID)
			{
				Reset();
				onActionDrag(arg);
				return;
			}

			FileLog.Log("!!! PBMods OnActionDrag");
			var actionEntity = PhantomBrigade.IDUtility.GetActionEntity(actionID);
			var (ok, movements) = Movement.GetPathedMovementActions(actionEntity);
			if (!ok)
			{
				Reset();
				onActionDrag(arg);
				return;
			}

			Movement.Edit(movements);
		}

		private static void Reset()
		{
			selectedActionID = unknownID;
			ResetScrubRangeLimit();
		}

		private static void OnActionDragEnd(object arg)
		{
			Reset();
			onActionDragEnd(arg);
		}

		internal static void OnActionSelected(int actionID)
		{
			var actionEntity = PhantomBrigade.IDUtility.GetActionEntity(actionID);
			if (actionEntity == null)
			{
				return;
			}

			var actionName = actionEntity.dataKeyAction.s;
			FileLog.Log($"!!! PBMods selected action: name={actionName}");

			if (!actionEntity.hasDataLinkActionMovement)
			{
				FileLog.Log("!!! PBMods selected action is not a movement action");
				return;
			}

			if (!actionEntity.hasMovementPath)
			{
				FileLog.Log("!!! PBMods selected action is not a pathed movement action");
				return;
			}

			if (!actionEntity.hasActionOwner)
			{
				return;
			}

			FileLog.Log($"!!! PBMods minimums: pathLength={Movement.pathLengthMinimum}; duration={Movement.PathLengthToDuration(actionEntity, Movement.pathLengthMinimum)}");
			FileLog.Log($"!!! PBMods selected movement action: {Movement.Stringify(actionEntity)}");

			// Only hits on the pointy part of the painted action will trigger this logic.
			// That's to avoid a drastic change when the end of the action snaps to where the mouse pointer is in the timeline.
			// The alternative is to warp the mouse pointer to the end of the action but that requires the InputSystem package
			// from Unity and is generally frowned upon.

			var actionEndTime = actionEntity.startTime.f + actionEntity.duration.f;
			var hitDelta = PhantomBrigade.CombatUtilities.ClampTimeInCurrentTurn(actionEndTime) - Contexts.sharedInstance.combat.predictionTimeTarget.f;
			if (hitDelta > paintedActionTipDuration)
			{
				FileLog.Log($"!!! PBMods missed trigger zone: delta={hitDelta}");
				return;
			}

			var (ok, movements) = Movement.GetPathedMovementActions(actionEntity);
			if (!ok)
			{
				return;
			}

			selectedActionID = actionID;
			Movement.CacheMovements(movements);
		}

		internal static void UpdateScrubbingTimeFix()
		{
			if (scrubTimeTargetMinimum == scrubTimeTargetMaximum)
			{
				return;
			}

			if (selectedActionID == unknownID)
			{
				return;
			}

			var actionEntity = PhantomBrigade.IDUtility.GetActionEntity(selectedActionID);
			if (actionEntity == null)
			{
				return;
			}

			var combat = Contexts.sharedInstance.combat;
			var timeTarget = Mathf.Clamp(combat.predictionTimeTarget.f, scrubTimeTargetMinimum, scrubTimeTargetMaximum);
			if (!Mathf.Approximately(combat.predictionTimeTarget.f, timeTarget))
			{
				combat.ReplacePredictionTimeTarget(timeTarget);
			}
		}
	}
}
