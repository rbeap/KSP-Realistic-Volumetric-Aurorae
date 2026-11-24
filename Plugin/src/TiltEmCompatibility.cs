// Realistic Volumetric Aurorae (RVA). Copyright (c) 2026 HereComesTheBoom ("Realistic Volumetric Aurorae")
// SPDX-License-Identifier: CC-BY-NC-SA-4.0  (see LICENSE.txt)
using System;
using System.Reflection;
using UnityEngine;

namespace RVA
{
    static class TiltEmCompatibility
    {
        static Func<CelestialBody, Vector3> getWorldNorth;
        static int nextSearchFrame;
        static bool warned;

        public static bool TryGetWorldNorth(CelestialBody body, out Vector3 north)
        {
            north = Vector3.up;
            if (body == null) return false;

            if (getWorldNorth == null)
            {
                if (Time.frameCount < nextSearchFrame) return false;
                nextSearchFrame = Time.frameCount + 120;
                FindWorldNorth();
            }

            if (getWorldNorth == null) return false;

            try
            {
                Vector3 axis = getWorldNorth(body);
                if (axis.sqrMagnitude < 1e-8f) return false;
                north = axis.normalized;
                return true;
            }
            catch (Exception e)
            {
                if (!warned)
                {
                    warned = true;
                    Debug.LogWarning($"[RVA] Tilt'Em axis lookup failed ({e.GetType().Name}); using stock orientation");
                }
                getWorldNorth = null;
                nextSearchFrame = int.MaxValue;
                return false;
            }
        }

        static void FindWorldNorth()
        {
            try
            {
                foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    Type axes = assembly.GetType("TiltEm.BodyAxes", false);
                    if (axes == null) continue;

                    MethodInfo method = axes.GetMethod("WorldNorth", BindingFlags.Public | BindingFlags.Static);
                    ParameterInfo[] parameters = method != null ? method.GetParameters() : new ParameterInfo[0];
                    if (method == null || method.ReturnType != typeof(Vector3)
                        || parameters.Length != 1 || parameters[0].ParameterType != typeof(CelestialBody))
                    {
                        warned = true;
                        nextSearchFrame = int.MaxValue;
                        Debug.LogWarning("[RVA] Tilt'Em is installed but its BodyAxes.WorldNorth API is incompatible; using stock orientation");
                        return;
                    }

                    getWorldNorth = (Func<CelestialBody, Vector3>)Delegate.CreateDelegate(
                        typeof(Func<CelestialBody, Vector3>), method);
                    if (AuroraSettings.DevBuild) Debug.Log("[RVA] Tilt'Em detected; aurorae will follow each body's axial tilt");
                    return;
                }
            }
            catch (Exception e)
            {
                if (!warned)
                {
                    warned = true;
                    Debug.LogWarning($"[RVA] Could not connect to Tilt'Em ({e.GetType().Name}); using stock orientation");
                }
                nextSearchFrame = int.MaxValue;
            }
        }
    }
}
