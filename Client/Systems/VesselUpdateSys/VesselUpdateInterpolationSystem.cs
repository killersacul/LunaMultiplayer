﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using LunaClient.Base;
using LunaClient.Network;
using LunaClient.Systems.Network;
using UnityEngine;

namespace LunaClient.Systems.VesselUpdateSys
{
    /// <summary>
    /// Main interpolation system class for vessel updates
    /// </summary>
    public class VesselUpdateInterpolationSystem : SubSystem<VesselUpdateSystem>
    {
        #region Fields

        /// <summary>
        /// After the value in ms specified here the vessel will be removed from the interpolation system
        /// </summary>
        private int MsWithoutUpdatesToRemove { get; } = 10000;

        /// <summary>
        /// The current vessel update that is being handled
        /// </summary>
        public Dictionary<Guid, VesselUpdate> CurrentVesselUpdate { get; } = new Dictionary<Guid, VesselUpdate>();

        #endregion

        /// <summary>
        /// Clear all the properties
        /// </summary>
        public void ResetSystem()
        {
            CurrentVesselUpdate.Clear();
        }

        /// <summary>
        /// Remove the vessels that didn't receive and update after the value specified in MsWithoutUpdatesToRemove every 5 seconds
        /// </summary>
        public IEnumerator RemoveVessels()
        {
            var seconds = new WaitForSeconds(5);
            while (true)
            {
                if (!System.Enabled) break;

                if (System.UpdateSystemReady)
                {
                    var vesselsToRemove = CurrentVesselUpdate
                        .Where(u => u.Value.InterpolationFinished &&
                                    TimeSpan.FromSeconds(Time.time - u.Value.FinishTime).TotalMilliseconds >
                                    MsWithoutUpdatesToRemove)
                        .Select(u => u.Key).ToArray();

                    foreach (var vesselId in vesselsToRemove)
                    {
                        CurrentVesselUpdate.Remove(vesselId);
                        System.ReceivedUpdates.Remove(vesselId);
                    }
                }

                yield return seconds;
            }
        }

        /// <summary>
        /// Main system that picks updates received and sets them for further processing. We call it in the 
        /// fixed update as in deals with phisics
        /// </summary>
        public IEnumerator HandleVesselUpdates()
        {
            var fixedUpdate = new WaitForFixedUpdate();
            while (true)
            {
                if (!System.Enabled) break;

                if (System.UpdateSystemReady)
                {
                    //Iterate over the updates that do not have interpolations going on
                    foreach (var vesselUpdates in System.ReceivedUpdates.Where(v => InterpolationFinished(v.Key) && v.Value.Count > 0))
                    {
                        var success = !CurrentVesselUpdate.ContainsKey(vesselUpdates.Key)
                            ? SetFirstVesselUpdates(GetValidUpdate(0, vesselUpdates.Value))
                            : HandleVesselUpdate(vesselUpdates);

                        if (success)
                            Client.Singleton.StartCoroutine(CurrentVesselUpdate[vesselUpdates.Key].ApplyVesselUpdate());
                    }
                }

                yield return fixedUpdate;
            }
        }

        /// <summary>
        /// Retrieves an update from the queue that was sent later than the one we have as target 
        /// and that was received close to "MSInthepast". Rmember to call it from fixed update only
        /// </summary>
        private static VesselUpdate GetValidUpdate(long targetSentTime, Queue<VesselUpdate> vesselUpdates)
        {
            var update = vesselUpdates.ToList()
                .Where(u => u.SentTime > targetSentTime && (Time.fixedTime - u.ReceiveTime) >= VesselCommon.SInPast)
                .OrderBy(u => Math.Abs((Time.fixedTime - u.ReceiveTime) - VesselCommon.SInPast))
                .FirstOrDefault();

            if (update != null)
            {
                var dequeued = vesselUpdates.Dequeue();
                while (dequeued.Id != update.Id)
                    dequeued = vesselUpdates.Dequeue();
            }

            return update;
        }

        private bool HandleVesselUpdate(KeyValuePair<Guid, Queue<VesselUpdate>> vesselUpdates)
        {
            var update = GetValidUpdate(CurrentVesselUpdate[vesselUpdates.Key].Target.SentTime, vesselUpdates.Value);
            if (update == null) return false;
            
            update.Vessel = CurrentVesselUpdate[vesselUpdates.Key].Vessel;
            if (CurrentVesselUpdate[vesselUpdates.Key].Target.BodyName == update.BodyName)
                update.Body = CurrentVesselUpdate[vesselUpdates.Key].Target.Body;

            CurrentVesselUpdate[vesselUpdates.Key] = CurrentVesselUpdate[vesselUpdates.Key].Target;
            CurrentVesselUpdate[vesselUpdates.Key].Target = update;

            return true;
        }

        /// <summary>
        /// Check if the given vesselId has finished it's interpolation
        /// </summary>
        /// <param name="vesselId"></param>
        /// <returns></returns>
        private bool InterpolationFinished(Guid vesselId)
        {
            if (CurrentVesselUpdate.ContainsKey(vesselId) && CurrentVesselUpdate[vesselId] != null)
            {
                return CurrentVesselUpdate[vesselId].InterpolationStarted && CurrentVesselUpdate[vesselId].InterpolationFinished;
            }
            return true;
        }

        /// <summary>
        /// Here we set the first vessel updates. We use the current vessel state as the starting point.
        /// </summary>
        /// <param name="update"></param>
        private bool SetFirstVesselUpdates(VesselUpdate update)
        {
            if (update == null) return false;

            var currentPosition = VesselUpdate.CreateFromVesselId(update.VesselId);
            if (currentPosition != null)
            {
                currentPosition.ReceiveTime = update.ReceiveTime - VesselCommon.SInPast;
                CurrentVesselUpdate.Add(update.VesselId, currentPosition);
                CurrentVesselUpdate[update.VesselId].Target = update;
                return true;
            }
            return false;
        }
    }
}
