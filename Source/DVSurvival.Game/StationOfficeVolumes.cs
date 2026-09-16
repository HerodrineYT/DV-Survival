using System;
using System.Collections.Generic;
using DV;
using UnityEngine;

namespace DVSurvival.Mod
{
    // Reuse the game's authored room volumes, not a radius around job terminals.
    internal sealed class StationOfficeVolumes
    {
        private readonly List<BoxCollider> rooms = new List<BoxCollider>();
        private float nextScan;

        public bool Contains(Vector3 position)
        {
            if (Time.realtimeSinceStartup >= nextScan)
            {
                nextScan = Time.realtimeSinceStartup + 3f;
                rooms.Clear();
                // Every native office interior has one or two precise AO boxes.
                // The same component is used by homes/lost-and-found buildings,
                // so accept only the verified office interior roots.
                foreach (var controller in UnityEngine.Object.FindObjectsOfType<PostProcessingVolumeAOController>())
                {
                    if (!IsOfficeInterior(controller.transform.parent)) continue;
                    foreach (var volume in controller.GetComponents<BoxCollider>())
                        if (!rooms.Contains(volume)) rooms.Add(volume);
                }
                // The tutorial's LFS office also has a dedicated detector.
                foreach (var detector in UnityEngine.Object.FindObjectsOfType<TutorialPlayerDetector>())
                {
                    if (detector.detectorType != TutorialPlayerDetector.TutorialPlayerDetectorType.StationOffice)
                        continue;
                    foreach (var volume in detector.GetComponentsInChildren<BoxCollider>(true))
                        if (!rooms.Contains(volume)) rooms.Add(volume);
                }
            }
            foreach (var room in rooms)
                if (Contains(room, position)) return true;
            return false;
        }

        private static bool IsOfficeInterior(Transform root)
        {
            if (root == null) return false;
            string name = root.name;
            if (name.EndsWith("(Clone)", StringComparison.Ordinal))
                name = name.Substring(0, name.Length - 7).TrimEnd();
            return name == "MilitaryOffice_interior" ||
                (name.Length == 17 && name.StartsWith("Office_", StringComparison.Ordinal) &&
                 name[7] >= '1' && name[7] <= '7' && name.EndsWith("_interior", StringComparison.Ordinal));
        }

        internal static bool Contains(BoxCollider room, Vector3 position)
        {
            if (room == null || !room.gameObject.activeInHierarchy) return false;
            // ClosestPoint on a disabled collider returns the input position.
            // Test the native box in its local space instead: this also handles
            // rotated offices and origin shifts without a stale world AABB.
            Vector3 relative = room.transform.InverseTransformPoint(position) - room.center;
            Vector3 half = room.size * .5f;
            return Mathf.Abs(relative.x) < Mathf.Abs(half.x) &&
                Mathf.Abs(relative.y) < Mathf.Abs(half.y) &&
                Mathf.Abs(relative.z) < Mathf.Abs(half.z);
        }
    }
}
