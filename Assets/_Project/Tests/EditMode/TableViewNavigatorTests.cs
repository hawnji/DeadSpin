using System;
using DeadSpin.Core.View;
using NUnit.Framework;

namespace DeadSpin.Tests
{
    public class TableViewNavigatorTests
    {
        private static TableViewNavigator Create()
        {
            // Overview, Layout, Wheel are free; the fourth (Game Master) is locked.
            return new TableViewNavigator(new[] { true, true, true, false }, 0.6f, 1.4f, 1.5f, 0.8f);
        }

        [Test]
        public void StartsOnFirstViewWithDefaultFraming()
        {
            var nav = Create();
            Assert.AreEqual(0, nav.CurrentView);
            Assert.AreEqual(1f, nav.Zoom);
            Assert.AreEqual(0f, nav.PanX);
        }

        [Test]
        public void NextAndPrevious_Wrap()
        {
            var nav = Create();
            nav.PreviousView();
            Assert.AreEqual(3, nav.CurrentView);
            nav.NextView();
            Assert.AreEqual(0, nav.CurrentView);
        }

        [Test]
        public void SelectView_RejectsInvalidIndex()
        {
            var nav = Create();
            Assert.IsFalse(nav.SelectView(4));
            Assert.IsFalse(nav.SelectView(-1));
            Assert.IsTrue(nav.SelectView(2));
            Assert.AreEqual(2, nav.CurrentView);
        }

        [Test]
        public void Zoom_IsClamped()
        {
            var nav = Create();
            nav.ZoomSteps(100f, 0.1f);
            Assert.AreEqual(0.6f, nav.Zoom, 1e-5);
            nav.ZoomSteps(-100f, 0.1f);
            Assert.AreEqual(1.4f, nav.Zoom, 1e-5);
        }

        [Test]
        public void Pan_IsClamped()
        {
            var nav = Create();
            nav.Pan(10f, -10f);
            Assert.AreEqual(1.5f, nav.PanX, 1e-5);
            Assert.AreEqual(-0.8f, nav.PanZ, 1e-5);
        }

        [Test]
        public void SwitchingView_ResetsFraming()
        {
            var nav = Create();
            nav.Pan(0.5f, 0.2f);
            nav.ZoomSteps(2f, 0.1f);
            nav.NextView();
            Assert.AreEqual(1f, nav.Zoom);
            Assert.AreEqual(0f, nav.PanX);
            Assert.AreEqual(0f, nav.PanZ);
        }

        [Test]
        public void LockedView_IgnoresPanAndZoom()
        {
            var nav = Create();
            nav.SelectView(3);
            nav.Pan(1f, 1f);
            nav.ZoomSteps(3f, 0.1f);
            Assert.AreEqual(0f, nav.PanX);
            Assert.AreEqual(1f, nav.Zoom);
        }

        [Test]
        public void ViewChanged_FiresOnlyOnRealChanges()
        {
            var nav = Create();
            int count = 0;
            nav.ViewChanged += i => count++;
            nav.SelectView(0);
            nav.SelectView(1);
            nav.SelectView(1);
            Assert.AreEqual(1, count);
        }

        [Test]
        public void InvalidConstruction_Throws()
        {
            Assert.Throws<ArgumentException>(() => new TableViewNavigator(new bool[0], 0.5f, 1.5f, 1f, 1f));
            Assert.Throws<ArgumentOutOfRangeException>(() => new TableViewNavigator(new[] { true }, 1.2f, 1.5f, 1f, 1f));
            Assert.Throws<ArgumentOutOfRangeException>(() => new TableViewNavigator(new[] { true }, 0.5f, 1.5f, -1f, 1f));
        }
    }
}
