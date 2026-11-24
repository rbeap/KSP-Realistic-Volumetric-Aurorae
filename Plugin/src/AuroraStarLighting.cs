// Realistic Volumetric Aurorae (RVA). Copyright (c) 2026 HereComesTheBoom ("Realistic Volumetric Aurorae")
// SPDX-License-Identifier: CC-BY-NC-SA-4.0  (see LICENSE.txt)
using System;
using System.Collections.Generic;
using UnityEngine;

namespace RVA
{

    public static class AuroraStarLighting
    {
        public const int MaxStars = 8;

        static readonly string SunDirsId = "_SunDirs";
        static readonly string SunCountId = "_SunCount";
        static readonly Vector4[] materialDirections = new Vector4[MaxStars];
        static readonly Dictionary<CelestialBody, Selection> selections = new Dictionary<CelestialBody, Selection>();

        class Selection
        {
            public string selector;
            public List<CelestialBody> sourceBodies;
            public int sourceCount;
            public readonly List<CelestialBody> stars = new List<CelestialBody>();
        }

        public static int Fill(CelestialBody body, string selector, bool scaledSpace, bool eclipses, Vector4[] directions)
        {
            if (directions == null || directions.Length < MaxStars)
                throw new ArgumentException($"Star direction buffer must hold at least {MaxStars} entries", nameof(directions));
            if (body == null) return 0;

            List<CelestialBody> stars = Resolve(body, selector);
            int count = 0;
            Transform bodyFrame = scaledSpace && body.scaledBody != null ? body.scaledBody.transform : body.transform;
            Vector3 bodyPosition = bodyFrame != null ? bodyFrame.position : (Vector3)body.position;

            foreach (var star in stars)
            {
                if (star == null || star == body) continue;
                Transform starFrame = scaledSpace && star.scaledBody != null ? star.scaledBody.transform : star.transform;
                if (starFrame == null) continue;
                Vector3 toStar = starFrame.position - bodyPosition;
                if (toStar.sqrMagnitude <= 1e-8f) continue;
                toStar.Normalize();
                float cover = eclipses ? AuroraEclipse.Coverage(body, star) : 0f;
                directions[count++] = new Vector4(toStar.x, toStar.y, toStar.z, 1f - cover);
                if (count == MaxStars) break;
            }
            return count;
        }

        public static void Apply(Material material, CelestialBody body, string selector, bool scaledSpace, bool eclipses)
        {
            if (material == null) return;
            int count = Fill(body, selector, scaledSpace, eclipses, materialDirections);
            material.SetVectorArray(SunDirsId, materialDirections);
            material.SetFloat(SunCountId, count);
        }

        static List<CelestialBody> Resolve(CelestialBody body, string selector)
        {
            List<CelestialBody> bodies = FlightGlobals.Bodies;
            string requested = string.IsNullOrWhiteSpace(selector) ? "auto" : selector.Trim();
            if (selections.TryGetValue(body, out Selection cached)
                && cached.sourceBodies == bodies
                && cached.sourceCount == (bodies != null ? bodies.Count : 0)
                && string.Equals(cached.selector, requested, StringComparison.OrdinalIgnoreCase))
                return cached.stars;

            var selection = new Selection
            {
                selector = requested,
                sourceBodies = bodies,
                sourceCount = bodies != null ? bodies.Count : 0
            };
            selections[body] = selection;

            if (bodies == null || bodies.Count == 0) return selection.stars;
            string[] names = requested.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            bool automatic = names.Length == 0 || (names.Length == 1 && string.Equals(names[0].Trim(), "auto", StringComparison.OrdinalIgnoreCase));
            if (!automatic && names.Length == 1 && (string.Equals(names[0].Trim(), "none", StringComparison.OrdinalIgnoreCase)
                                                      || string.Equals(names[0].Trim(), "off", StringComparison.OrdinalIgnoreCase)))
                return selection.stars;

            if (automatic)
            {
                CelestialBody primary = NearestStarAncestor(body);
                if (primary != null) AddStar(selection, primary);
                if (selection.stars.Count == 0)
                    Debug.LogWarning($"[RVA:Stars] {body.bodyName}: no star found among its reference-body ancestors; aurora emission remains enabled, use stars = StarName only if this body's system does not parent it to its star");
            }
            else
            {
                foreach (string value in names)
                {
                    string name = value.Trim();
                    if (name.Length == 0 || string.Equals(name, "auto", StringComparison.OrdinalIgnoreCase)) continue;
                    CelestialBody found = null;
                    foreach (var candidate in bodies)
                    {
                        if (candidate != null && string.Equals(candidate.bodyName, name, StringComparison.OrdinalIgnoreCase))
                        {
                            found = candidate;
                            break;
                        }
                    }
                    if (found == null || !found.isStar)
                        Debug.LogWarning($"[RVA:Stars] {body.bodyName}: configured star '{name}' was not found or is not marked as a star");
                    else AddStar(selection, found);
                }
            }

            if (selection.stars.Count > MaxStars)
            {
                Debug.LogWarning($"[RVA:Stars] {body.bodyName}: found {selection.stars.Count} stars; only the first {MaxStars} will light the aurora");
                selection.stars.RemoveRange(MaxStars, selection.stars.Count - MaxStars);
            }
            if (selection.stars.Count > 0)
            {
                var starNames = new string[selection.stars.Count];
                for (int i = 0; i < starNames.Length; i++) starNames[i] = selection.stars[i].bodyName;
                if (AuroraSettings.DevBuild) Debug.Log($"[RVA:Stars] {body.bodyName}: using {string.Join(", ", starNames)} ({(automatic ? "automatic" : "configured")})");
            }
            return selection.stars;
        }

        static void AddStar(Selection selection, CelestialBody star)
        {
            if (!selection.stars.Contains(star)) selection.stars.Add(star);
        }

        static CelestialBody NearestStarAncestor(CelestialBody body)
        {
            var seen = new HashSet<CelestialBody>();
            CelestialBody current = body != null ? body.referenceBody : null;
            while (current != null && seen.Add(current))
            {
                if (current.isStar) return current;
                current = current.referenceBody;
            }
            return null;
        }
    }
}
