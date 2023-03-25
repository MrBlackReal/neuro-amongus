﻿using BepInEx;
using BepInEx.Configuration;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Reactor;
using UnityEngine;
using System.Collections.Generic;
using System.Collections;
using System.Text.Json;

namespace Neuro;

[BepInAutoPlugin]
[BepInProcess("Among Us.exe")]
[BepInDependency(ReactorPlugin.Id)]
public partial class NeuroPlugin : BasePlugin
{
    public Harmony Harmony { get; } = new(Id);

    public ConfigEntry<string> ConfigName { get; private set; }

    public Recorder recorder = new Recorder();

    public Vision vision = new Vision();

    public bool inMinigame = false;

    public bool hasStarted = false;

    public Pathfinding pathfinding = new Pathfinding();

    public Vector2 directionToNearestTask = new Vector2();
    public Vector2 moveDirection = new Vector2();

    public Vector2[] currentPath = new Vector2[0];
    public int pathIndex = -1;

    public LineRenderer arrow;

    public List<PlayerTask> tasks = new List<PlayerTask>();

    public bool didKill = false;
    public bool didReport = false;
    public bool didVent = false;

    public override void Load()
    {
        ConfigName = Config.Bind("Neuro", "Name", "Neuro-sama");

        Harmony.PatchAll();
    }

    public void StartMap(ShipStatus shipStatus)
    {
        Debug.Log("OnShipLoad");
        pathfinding.GenerateNodeGrid();

        pathfinding.FloodFill(shipStatus.MeetingSpawnCenter + (Vector2.up * shipStatus.SpawnRadius) + new Vector2(0f, 0.3636f));

        GameObject arrowGo = new GameObject("Arrow");
        arrow = arrowGo.AddComponent<LineRenderer>();
        arrow.startWidth = 0.4f;
        arrow.endWidth = 0.05f;
        arrow.positionCount = 2;
        arrow.material = new Material(Shader.Find("Sprites/Default"));
        arrow.startColor = Color.blue;
        arrow.endColor = Color.cyan;
    }

    public void FixedUpdate(PlayerControl localPlayer)
    {
        if (MeetingHud.Instance != null && MeetingHud.Instance.enabled) return;

        vision.UpdateVision();

        if (localPlayer.myTasks != null)
        {
            foreach (PlayerTask task in localPlayer.myTasks)
            {
                // Must be in this order or else breaks Wires task
                if (task == null) continue;
                if (task.IsComplete || inMinigame) continue;
                if (task.Locations == null) continue;

                foreach (Vector2 location in task.Locations)
                {
                    if (Vector2.Distance(location, PlayerControl.LocalPlayer.transform.position) < 0.8f)
                    {
                        if (task.MinigamePrefab)
                        {
                            var minigame = GameObject.Instantiate(task.GetMinigamePrefab());
                            minigame.transform.SetParent(Camera.main.transform, false);
                            minigame.transform.localPosition = new Vector3(0f, 0f, -50f);
                            minigame.Begin(task);
                            inMinigame = true;
                        }
                    }
                }
            }
        }

        bool sabotageActive = false;
        foreach (PlayerTask task in localPlayer.myTasks)
            if (task.TaskType == TaskTypes.FixLights || task.TaskType == TaskTypes.RestoreOxy || task.TaskType == TaskTypes.ResetReactor || task.TaskType == TaskTypes.ResetSeismic || task.TaskType == TaskTypes.FixComms)
                sabotageActive = true;

        // Record values
        Frame frame = new Frame(
            localPlayer.Data.RoleType == AmongUs.GameOptions.RoleTypes.Impostor,
            localPlayer.killTimer,
            directionToNearestTask,
            sabotageActive,
            Vector2.zero,
            vision.directionToNearestBody,
            GameManager.Instance.CanReportBodies() && HudManager.Instance.ReportButton.canInteract,
            vision.playerRecords,
            moveDirection,
            didReport,
            didVent,
            didKill,
            // TODO: Implement these two
            false,
            false
        );

        didKill = false;
        didReport = false;
        didVent = false;

        //string frameString = JsonSerializer.Serialize(frame);
        //Debug.Log(frameString);

        recorder.Frames.Add(frame);
    }

    public bool MovePlayer(ref Vector2 direction)
    {
        moveDirection = direction;
        if (currentPath.Length > 0 && pathIndex != -1)
        {
            Vector2 nextWaypoint = currentPath[pathIndex];

            while (Vector2.Distance(PlayerControl.LocalPlayer.GetTruePosition(), nextWaypoint) < 0.75f)
            {
                pathIndex++;
                if (pathIndex > currentPath.Length - 1)
                {
                    pathIndex = currentPath.Length - 1;
                    nextWaypoint = currentPath[pathIndex];
                    break;
                }

                nextWaypoint = currentPath[pathIndex];
            }

            directionToNearestTask = (nextWaypoint - (Vector2)PlayerControl.LocalPlayer.GetTruePosition()).normalized;


            LineRenderer renderer = arrow;
            renderer.SetPosition(0, PlayerControl.LocalPlayer.GetTruePosition());
            renderer.SetPosition(1, PlayerControl.LocalPlayer.GetTruePosition() + directionToNearestTask);
        }
        else
        {
            directionToNearestTask = Vector2.zero;
        }

        return true;
    }

    public void MeetingBegin()
    {
        Debug.Log("NEURO: MEETING CALLED");
        vision.ReportFindings();
    }

    public void MeetingEnd()
    {
        Debug.Log("NEURO: MEETING IS FINISHED");
        vision.MeetingEnd();
    }
    public IEnumerator EvaluatePath(NormalPlayerTask initial)
    {
        currentPath = pathfinding.FindPath(PlayerControl.LocalPlayer.transform.position, initial.Locations[0]);
        pathIndex = 0;

        while (true)
        {
            yield return new WaitForSeconds(0.5f);

            UpdatePathToTask();
        }
    }

    public void UpdatePathToTask(PlayerTask task = null)
    {
        if(task == null) task = PlayerControl.LocalPlayer.myTasks[0];

        PlayerTask nextTask = null;
        if (task.IsComplete)
        {
            Debug.Log("Task is complete, getting next one.");
            PlayerTask closestTask = null;
            float closestDistance = Mathf.Infinity;

            foreach (PlayerTask t in PlayerControl.LocalPlayer.myTasks)
            {
                if (!t.IsComplete && t.HasLocation)
                {
                    Vector2[] path = pathfinding.FindPath(PlayerControl.LocalPlayer.transform.position, t.Locations[0]);
                    // Evaluate length of path
                    float distance = 0f;
                    for (int i = 0; i < path.Length - 1; i++)
                    {
                        distance += Vector2.Distance(path[i], path[i + 1]);
                    }

                    if (distance < closestDistance)
                    {
                        closestDistance = distance;
                        closestTask = t;
                    }
                }
            }
            if(closestTask == null)
            {
                pathIndex = -1;
                GameObject.Destroy(GameObject.Find("Arrow"));
                return;
            }
            nextTask = closestTask;
        }
        else
        {
            nextTask = task;
        }
        if (nextTask != null)
        {
            Debug.Log("Next task isn't null");
            currentPath = pathfinding.FindPath(PlayerControl.LocalPlayer.transform.position, nextTask.Locations[0]);
            pathIndex = 0;

            //pathfinding.DrawPath(currentPath);
        }
    }
}
