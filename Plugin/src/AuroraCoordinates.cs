// Realistic Volumetric Aurorae (RVA). Copyright (c) 2026 HereComesTheBoom ("Realistic Volumetric Aurorae")
// SPDX-License-Identifier: CC-BY-NC-SA-4.0  (see LICENSE.txt)
using UnityEngine;

namespace RVA
{
    public static class AuroraCoordinates
    {
        public static Quaternion WorldToAurora(Transform bodyFrame, CelestialBody body)
        {
            Vector3 pole;
            if (body == null || !TiltEmCompatibility.TryGetWorldNorth(body, out pole))
            {
                if (bodyFrame == null) return Quaternion.identity;
                pole = bodyFrame.up;
            }
            if (pole.sqrMagnitude < 1e-6f) return Quaternion.identity;
            return Quaternion.FromToRotation(pole.normalized, Vector3.up);
        }
    }
}
