using System;
using System.Collections.Generic;

namespace NuclearTriad.Geometry
{
    /// <summary>
    /// The sideways eye slide in cockpit free look. Past about 33° of pan the game slides the eye
    /// toward the side being looked at, reaching 20 cm at 100°, like leaning. On a triple rig the
    /// nose stays on a wing screen out to the wing's outer edge, and the slide makes the near
    /// cockpit shift under the flight HUD there. Held, the eye stays still while the nose is on a
    /// screen, and the game's ramp, at its own slope, starts once the nose has left the outer edge.
    /// </summary>
    public static class FreeLook
    {
        const double FullSlideM = 0.2;
        const double SlidePerDegree = 0.015;

        /// <summary>The game's slide in metres for a pan angle (positive right), toward the side looked at.</summary>
        public static double GameSlide(double panDeg) =>
            FullSlideM * Clamp01(Math.Abs(panDeg) * SlidePerDegree - 0.5) * Math.Sign(panDeg);

        /// <summary>The held slide: none until the pan passes the reach on that side, then the game's ramp from there.</summary>
        public static double HeldSlide(double panDeg, double reachRightDeg, double reachLeftDeg)
        {
            double reach = panDeg >= 0 ? reachRightDeg : reachLeftDeg;
            return FullSlideM * Clamp01((Math.Abs(panDeg) - reach) * SlidePerDegree) * Math.Sign(panDeg);
        }

        /// <summary>
        /// How far the view can pan right and left before the nose, straight ahead of the eye at
        /// zero pan, leaves the outermost screen on the other side: the yaw of each outer edge.
        /// </summary>
        public static (double RightDeg, double LeftDeg) Reach(IList<Panel> panels)
        {
            double Yaw(Vec3 v) => Math.Atan2(v.X, v.Z) * 180 / Math.PI;
            Panel left = panels[0], right = panels[panels.Count - 1];
            // Panning right brings the nose onto the left screens, and left onto the right ones.
            return (-Yaw(left.LowerLeft), Yaw(right.LowerRight));
        }

        static double Clamp01(double t) => t < 0 ? 0 : t > 1 ? 1 : t;
    }
}
