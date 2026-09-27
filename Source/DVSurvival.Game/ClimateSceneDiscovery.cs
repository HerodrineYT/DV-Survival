using System;
using System.Collections.Generic;
using DV;
using DV.Simulation.Cars;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DVSurvival.Mod
{
    internal static class ClimateSceneDiscovery
    {
        private static bool listening;
        private static readonly List<GameObject> roots = new List<GameObject>();
        private static readonly List<PostProcessingVolumeAOController> offices = new List<PostProcessingVolumeAOController>();
        private static readonly List<TutorialPlayerDetector> detectors = new List<TutorialPlayerDetector>();
        private static readonly List<FireboxSimController> fireboxes = new List<FireboxSimController>();

        internal static void Start()
        {
            if (listening) return;
            listening = true;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        internal static void Stop()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            listening = false;
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            // Inspect only the new sector, including disabled native office components.
            // Initial discovery and missed third-party callbacks retain the 30 s fallback.
            try
            {
                scene.GetRootGameObjects(roots);
                foreach (var root in roots)
                {
                    root.GetComponentsInChildren(true, offices);
                    foreach (var office in offices) StationOfficeVolumes.Register(office);
                    root.GetComponentsInChildren(true, detectors);
                    foreach (var detector in detectors) StationOfficeVolumes.Register(detector);
                    root.GetComponentsInChildren(true, fireboxes);
                    foreach (var firebox in fireboxes) FireboxRegistry.Register(firebox);
                }
            }
            catch (Exception error) { ClimateRegistrationErrors.Report(error); }
            finally { roots.Clear(); offices.Clear(); detectors.Clear(); fireboxes.Clear(); }
        }
    }
}
