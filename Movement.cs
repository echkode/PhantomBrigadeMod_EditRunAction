﻿using System.Collections.Generic;
using System.Linq;

using HarmonyLib;
using PhantomBrigade.Data;
using UnityEngine;

namespace EchKode.PBMods.EditRunAction
{
	static class Movement
	{
		private class Cache
		{
			public List<float> Durations = new List<float>();
			public List<Vector3> Points = new List<Vector3>();
			public List<Area.AreaNavLink> Links = new List<Area.AreaNavLink>();

			public float TotalLength;
			public float TotalDuration;

			public void Clear()
			{
				Durations.Clear();
				Points.Clear();
				Links.Clear();
				TotalLength = 0f;
				TotalDuration = 0f;
			}
		}

		private class Accumulator
		{
			public ActionEntity Action;
			public List<Vector3> Points;
			public List<Area.AreaNavLink> Links;
			public float Duration;
			public float AccumulatedLength;
		}

		// Constant pulled from the code (ActionUtility.TrimPositionalActionsFromTime)
		internal const float pathLengthMinimum = 1.5f;

		private static Cache cachedMovements;
		private static float currentEndTime;

		internal static string Stringify(ActionEntity movement)
		{
			var movementType = movement.dataLinkActionCore.data.trackType == PhantomBrigade.Data.TrackType.Double
				? movement.hasMovementDash
					? "dash"
					: "melee"
				: "normal";
			PhantomBrigade.CombatUIUtility.GetTimelineNormalizedValue(movement.startTime.f, out var startInTurn);
			var duration = movement.hasDuration ? movement.duration.f : 0f;
			var rawLength = (float)GetPathLength(movement.movementPath.points);
			var processedLength = movement.hasMovementPathProcessed ? movement.movementPathProcessed.length : 0f;
			var isStart = movement.hasMovementPathProcessed && movement.movementPathProcessed.start;
			var isEnd = movement.hasMovementPathProcessed && movement.movementPathProcessed.end;
			var processedPointCount = movement.hasMovementPathProcessed ? movement.movementPathProcessed.points.Count : 0;
			var processedLinkCount = movement.hasMovementPathProcessed ? movement.movementPathProcessed.links.Count : 0;
			var directionCount = movement.hasMovementPathProcessed ? movement.movementPathProcessed.directions.Count : 0;
			return $"{{{movement.id.id};{movement.dataKeyAction.s};{movementType};locked={movement.isLocked};startTime={movement.startTime.f}/{startInTurn};duration={duration};isPathStart={isStart};isPathEnd={isEnd};length={rawLength}/{processedLength};points={movement.movementPath.points.Count}/{processedPointCount};links={movement.movementPath.links.Count}/{processedLinkCount};directions={directionCount}}}";
		}

		internal static void Initialize()
		{
			cachedMovements = new Cache();
		}

		internal static (bool Ok, List<ActionEntity> Movements) GetPathedMovementActions(ActionEntity selectedAction)
		{
			var combatUnit = Contexts.sharedInstance.combat.GetEntityWithId(selectedAction.actionOwner.combatID);
			if (!combatUnit.isPlayerControllable)
			{
				FileLog.Log("!!! PBMods owner of selected action is not controllable");
				return (false, null);
			}
			if (!PhantomBrigade.CombatUIUtility.IsUnitFriendly(combatUnit))
			{
				FileLog.Log("!!! PBMods owner of selected action is not friendly");
				return (false, null);
			}

			var entitiesWithActionOwner = Contexts.sharedInstance.action.GetEntitiesWithActionOwner(combatUnit.id.id);
			var movements = new List<ActionEntity>();
			foreach (var action in entitiesWithActionOwner)
			{
				if (action.isDisposed)
				{
					continue;
				}

				if (action.CompletedAction)
				{
					continue;
				}

				if (!action.hasMovementPath)
				{
					continue;
				}

				if (action.startTime.f < selectedAction.startTime.f)
				{
					continue;
				}

				if (action.dataLinkActionCore.data.trackType == TrackType.Double)
				{
					FileLog.Log($"!!! PBMods found double track action after selected action: name={action.dataKeyAction.s}");
					return (false, null);
				}

				FileLog.Log($"!!! PBMods added movement action: {Stringify(action)}");
				movements.Add(action);
			}

			if (movements.Count == 0)
			{
				FileLog.Log($"!!! PBMods no valid movement actions for unit P-{combatUnit.id.id}");
				return (false, null);
			}

			movements.Sort((actionA, actionB) => actionA.startTime.f.CompareTo(actionB.startTime.f));
			return (true, movements);
		}

		internal static void CacheMovements(List<ActionEntity> movements)
		{
			var k = 0;
			cachedMovements.Clear();
			var first = true;
			foreach (var movement in movements)
			{
				cachedMovements.Durations.Add(movement.duration.f);

				if (!first)
				{
					// Movement paths should share one point (last point for the one before, first point for the one after).
					cachedMovements.Points.RemoveAt(cachedMovements.Points.Count - 1);
				}
				first = false;

				FileLog.Log($"!!! PBMods adding path points to cache: action={movement.id.id}; points={movement.movementPath.points.Count}; length={GetPathLength(movement.movementPath.points)}; duration={movement.duration.f}");
				k = 0;
				foreach (var point in movement.movementPath.points)
				{
					k += 1;
					FileLog.Log($"\t{k}: {point}");
				}
				FileLog.Log($"!!! PBMods adding path links to cache: action={movement.id.id}");
				k = 0;
				foreach (var link in movement.movementPath.links)
				{
					k += 1;
					FileLog.Log($"\t{k}: {link.type}:{link.destinationIndex}");
				}

				cachedMovements.Points.AddRange(movement.movementPath.points);
				cachedMovements.Links.AddRange(movement.movementPath.links);
			}

			cachedMovements.TotalLength = (float)GetPathLength(cachedMovements.Points);
			cachedMovements.TotalDuration = cachedMovements.Durations.Sum();

			var selected = movements.First();
			currentEndTime = selected.startTime.f + selected.duration.f;

			FileLog.Log($"!!! PBMods cached movement totals: actions={cachedMovements.Durations.Count}; points={cachedMovements.Points.Count}; links={cachedMovements.Links.Count}; length={cachedMovements.TotalLength}; duration={cachedMovements.TotalDuration}");
			FileLog.Log("!!! PBMods cached path points");
			k = 0;
			foreach (var point in cachedMovements.Points)
			{
				k += 1;
				FileLog.Log($"\t{k}: {point}");
			}
			FileLog.Log("!!! PBMods cached path links");
			k = 0;
			foreach (var link in cachedMovements.Links)
			{
				k += 1;
				FileLog.Log($"\t{k}: {link.type}:{link.destinationIndex}");
			}
		}

		private static double GetPathLength(List<Vector3> vectorPath)
		{
			double pathLength = 0.0;
			if (vectorPath != null)
			{
				for (var index = 1; index < vectorPath.Count; index += 1)
				{
					var v1 = vectorPath[index - 1];
					var v2 = vectorPath[index];
					pathLength += Distance(v1.x, v1.y, v1.z, v2.x, v2.y, v2.z);
				}
			}
			return pathLength;
		}

		internal static void Edit(List<ActionEntity> movements)
		{
			var selected = movements.First();
			var cachedDuration = cachedMovements.Durations.First();
			var clampedStartTime = PhantomBrigade.CombatUtilities.ClampTimeInCurrentTurn(selected.startTime.f + PathLengthToDuration(selected, pathLengthMinimum) + Timeline.paintedActionTipDuration);
			var clampedEndTime = PhantomBrigade.CombatUtilities.ClampTimeInCurrentTurn(selected.startTime.f + cachedDuration);

			Timeline.scrubTimeTargetMinimum = clampedStartTime;
			Timeline.scrubTimeTargetMaximum = clampedEndTime;

			var combat = Contexts.sharedInstance.combat;
			var timeTarget = combat.predictionTimeTarget.f;
			var durationChange = Contexts.sharedInstance.combat.predictionTimeTarget.f - currentEndTime;
			FileLog.Log($"!!! PBMods move edit times: timeTarget={timeTarget}; change={durationChange}; actionStart={selected.startTime.f}; clampedStart={clampedStartTime}; clampedEnd={clampedEndTime}");

			if (timeTarget < clampedStartTime)
			{
				combat.ReplacePredictionTimeTarget(clampedStartTime);
				return;
			}

			if (clampedEndTime < timeTarget)
			{
				// XXX May have an issue where we need one drag event past the end to get the path to restore fully to the end.
				combat.ReplacePredictionTimeTarget(clampedEndTime);
				return;
			}

			if (UtilityMath.RoughlyEqual(durationChange, 0f, 0.005f))
			{
				return;
			}

			currentEndTime = Contexts.sharedInstance.combat.predictionTimeTarget.f;
			ChangePath(durationChange, movements);
		}

		internal static float PathLengthToDuration(ActionEntity selected, float pathLength)
		{
			var combatEntity = PhantomBrigade.IDUtility.GetCombatEntity(selected.actionOwner.combatID);
			var f = combatEntity.movementSpeedCurrent.f;
			var dataMovement = selected.dataLinkActionMovement.data;
			var num1 = dataMovement != null ? dataMovement.movementSpeedScalar : 1f;
			return pathLength / (f * num1);
		}

		private static void ChangePath(float durationChange, List<ActionEntity> movements)
		{
			FileLog.Log("!!! PBMods change path");

			var selected = movements.First();
			var pathLength = (float)GetPathLength(selected.movementPath.points);
			if (durationChange < 0f && pathLength <= pathLengthMinimum)
			{
				FileLog.Log("!!! PBMods shrink early exit, path at minimum");
				return;
			}

			var duration = selected.duration.f + durationChange;
			var (length, points, links) = ProcessAndTrimPath(
				cachedMovements.Points,
				cachedMovements.Links,
				cachedMovements.TotalLength,
				PathLengthToDuration(selected, cachedMovements.TotalLength),
				duration,
				0f);

			if (length < pathLengthMinimum)
			{
				return;
			}

			var segments = new List<Accumulator>()
			{
				new Accumulator()
				{
					Action = selected,
					Points = points,
					Links = links,
					Duration = duration,
					AccumulatedLength = length,
				},
			};

			for (var i = 1;  i < movements.Count; i += 1)
			{
				segments.Add(AccumulateMovement(segments, movements[i], i));
			}

			UpdateMovementsFromSegments(selected.startTime.f, segments);
		}

		private static Accumulator AccumulateMovement(
			List<Accumulator> segments,
			ActionEntity movement,
			int index)
		{
			var duration = cachedMovements.Durations[index];
			var accumulatedLength = segments[index - 1].AccumulatedLength;
			FileLog.Log($"!!! PBMods accumulate path: index={index}; action={movement.id.id}; acc={accumulatedLength}; duration={duration}");
			var (length, points, links) = ProcessAndTrimPath(
				cachedMovements.Points,
				cachedMovements.Links,
				cachedMovements.TotalLength,
				PathLengthToDuration(movement, cachedMovements.TotalLength),
				duration,
				accumulatedLength);

			if (points.Count != links.Count + 1)
			{
				FileLog.Log($"!!! PBMods points/links count mismatch: action={movement.id.id}; points={points.Count}; links={links.Count}; duration={duration}");
			}

			return new Accumulator()
			{
				Action = movement,
				Points = points,
				Links = links,
				Duration = duration,
				AccumulatedLength = accumulatedLength + length,
			};
		}

		private static (float, List<Vector3>, List<Area.AreaNavLink>)
			ProcessAndTrimPath(
			  List<Vector3> inputPoints,
			  List<Area.AreaNavLink> inputLinks,
			  float inputPathLength,
			  float inputPathDuration,
			  float actionDurationClamped,
			  float lengthOfLastPath)
		{
			// Taken from ActionUtility.ProcessAndTrimPath() which is both private and has a
			// less than ideal call signature.

			var outputLength = 0.0;
			var outputPoints = new List<Vector3>(inputPoints.Count);
			var outputLinks = new List<Area.AreaNavLink>(inputLinks.Count);

			var endLength = System.Math.Round((double)inputPathLength * actionDurationClamped / inputPathDuration + lengthOfLastPath, 3);
			FileLog.Log($"!!! PBMods trim path: length={inputPathLength}; duration={inputPathDuration}; clamp={actionDurationClamped}; lastPath={lengthOfLastPath}; end={endLength}");
			var currentLength = 0.0;
			var lastLength = System.Math.Round((double)lengthOfLastPath, 3);

			for (var index = 1; index < inputPoints.Count; index += 1)
			{
				var inputPoint1 = inputPoints[index - 1];
				var inputPoint2 = inputPoints[index];
				//var linkLength = Vector3.Distance(inputPoint1, inputPoint2);
				var linkLength = Distance(inputPoint1.x, inputPoint1.y, inputPoint1.z, inputPoint2.x, inputPoint2.y, inputPoint2.z);
				FileLog.Log($"!!! PBMods distance calc: p1={inputPoint1}; p2={inputPoint2}; d={linkLength}");
				var inputLink = inputLinks[index - 1];
				var accumulatedLength = currentLength;
				currentLength += linkLength;
				var atStart = outputPoints.Count == 0;
				var atEnd = RoughlyEqual(currentLength, endLength);

				if (currentLength < lastLength || RoughlyEqual(currentLength, lastLength))
				{
					continue;
				}

				if (atStart)
				{
					var p1 = inputPoint1;
					var p2 = inputPoint2;
					var s1 = $"{index}";
					var s2 = $"{index + 1}";
					var len = linkLength;
					var isLast = false;
					if (RoughlyEqual(accumulatedLength, lastLength))
					{
						FileLog.Log($"!!! PBMods adding first link @ exact start: prior={lastLength}; acc={accumulatedLength}; end={endLength}");
					}
					else if (accumulatedLength < lastLength)
					{
						FileLog.Log($"!!! PBMods first link @ interpolated start: prior={lastLength}; acc={accumulatedLength}; end={endLength}");
						var fragment = currentLength - lengthOfLastPath;
						var t = 1.0 - fragment / linkLength;
						p1 = Vector3.Lerp(inputPoint1, inputPoint2, (float)t);
						s1 = "interpolated";
						len = Distance(p1.x, p1.y, p1.z, p2.x, p2.y, p2.z);
					}

					if (currentLength > endLength || atEnd)
					{
						var num7 = endLength - accumulatedLength;
						var t = num7 / linkLength;
						p2 = Vector3.Lerp(inputPoint1, inputPoint2, (float)t);
						s2 = "interpolated";
						len = Distance(p1.x, p1.y, p1.z, p2.x, p2.y, p2.z);
						isLast = true;
					}

					outputPoints.Add(p1);
					FileLog.Log($"\t{outputPoints.Count}: {p1} ({s1})");
					outputPoints.Add(p2);
					FileLog.Log($"\t{outputPoints.Count}: {p2} ({s2})");
					outputLinks.Add(inputLink);
					if (outputPoints.Count != outputLinks.Count + 1)
					{
						FileLog.Log($"!!! PBMods points/links desync @ exact start: index={index}; points={outputPoints.Count}; links={outputLinks.Count}");
					}
					outputLength += len;
					if (isLast)
					{
						break;
					}
				}
				else if (currentLength < endLength || atEnd)
				{
					FileLog.Log($"!!! PBMods link: acc={accumulatedLength}; current={currentLength}; end={endLength}; atEnd={atEnd}");
					outputPoints.Add(inputPoint2);
					FileLog.Log($"\t{outputPoints.Count}: {inputPoint2} ({index + 1})");
					outputLinks.Add(inputLink);
					outputLength += linkLength;
					if (atEnd)
					{
						break;
					}
				}
				else
				{
					FileLog.Log($"!!! PBMods last link: acc={accumulatedLength}; current={currentLength}; end={endLength}");
					var num7 = endLength - accumulatedLength;
					var t = num7 / linkLength;
					var b = Vector3.Lerp(inputPoint1, inputPoint2, (float)t);
					outputPoints.Add(b);
					FileLog.Log($"\t{outputPoints.Count}: {b} (interpolated)");
					outputLinks.Add(inputLink);
					if (outputPoints.Count != outputLinks.Count + 1)
					{
						FileLog.Log($"!!! PBMods points/links desync @ interpolated end: index={index}; points={outputPoints.Count}; links={outputLinks.Count}");
					}
					outputLength += Distance(inputPoint1.x, inputPoint1.y, inputPoint1.z, b.x, b.y, b.z);
					break;
				}
			}

			FileLog.Log($"!!! PBMods trim path: length={outputLength}; points={outputPoints.Count}; links={outputLinks.Count}");
			FileLog.Log("!!! PBMods output path points");
			var k = 0;
			foreach (var point in outputPoints)
			{
				k += 1;
				FileLog.Log($"\t{k}: {point}");
			}
			return ((float)outputLength, outputPoints, outputLinks);
		}

		private static double Distance(double x1, double y1, double z1, double x2, double y2, double z2)
		{
			var x = System.Math.Round(x1 - x2, 2);
			var y = System.Math.Round(y1 - y2, 2);
			var z = System.Math.Round(z1 - z2, 2);
			return System.Math.Round(System.Math.Sqrt(x * x + y * y + z * z), 3);
		}

		private static bool RoughlyEqual(double a, double b) => System.Math.Abs(a - b) < 0.01;

		private static float UpdateMovementsFromSegments(float startTime, List<Accumulator> segments)
		{
			foreach (var segment in segments)
			{
				if (segment.Points.Count == 0)
				{
					FileLog.Log($"!!! PBMods attempting to update movement with 0 path points: actionID={segment.Action.id.id}");
				}
				if (segment.Links.Count == 0)
				{
					FileLog.Log($"!!! PBMods attempting to update movement with 0 link points: actionID={segment.Action.id.id}");
				}

				UpdateMovement(
					segment.Action,
					segment.Points,
					segment.Links,
					startTime,
					segment.Duration);
				startTime += segment.Duration;
			}

			return startTime;
		}

		private static void UpdateMovement(
			ActionEntity selected,
			List<Vector3> points,
			List<Area.AreaNavLink> links,
			float startTime,
			float duration)
		{
			selected.ReplaceMovementPath(points, links);
			selected.isMovementPathChanged = true;
			selected.ReplaceStartTime(startTime);
			selected.ReplaceDuration(duration);
		}
	}
}
