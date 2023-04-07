﻿using Neuro.Utilities.Collections;
using UnityEngine;

namespace Neuro.Vision.DeadBodies;

public sealed class DeadBodyVisionData
{
    public byte ParentId { get; init; }
    public Vector2 LastSeenPosition { get; init; }
    public float FirstSeenTime { get; init; }
    public SelfUnstableList<PlayerControl> NearbyPlayers { get; } = new();

    private DeadBodyVisionData(byte parentId, Vector2 lastSeenPosition, float firstSeenTime)
    {
        ParentId = parentId;
        LastSeenPosition = lastSeenPosition;
        FirstSeenTime = firstSeenTime;
    }

    public static DeadBodyVisionData Create(DeadBody deadBody)
    {
        DeadBodyVisionData data = new(deadBody.ParentId, deadBody.TruePosition, Time.fixedTime);

        // Look for nearby players that could have killed the body.
        foreach (PlayerControl potentialWitness in PlayerControl.AllPlayerControls)
        {
            if (potentialWitness.AmOwner) continue;
            if (potentialWitness.inVent || potentialWitness.Data.IsDead) continue;

            if (!Visibility.IsVisible(potentialWitness)) continue;

            // If a witness is closer to the body than to neuro, there is a chance they did the kill.
            float distanceBetweenWitnessAndBody = Vector2.Distance(potentialWitness.GetTruePosition(), deadBody.TruePosition);
            float distanceBetweenWitnessAndNeuro = Vector2.Distance(potentialWitness.GetTruePosition(), PlayerControl.LocalPlayer.GetTruePosition());

            if (distanceBetweenWitnessAndBody < distanceBetweenWitnessAndNeuro) data.NearbyPlayers.Add(potentialWitness);
        }

        return data;
    }
}