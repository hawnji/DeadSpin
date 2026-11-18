using System.Collections.Generic;
using DeadSpin.Core.GameState;
using NUnit.Framework;

namespace DeadSpin.Tests
{
    public class GameStateMachineTests
    {
        [Test]
        public void StartsInBoot_AndOnlyAllowsExploring()
        {
            var machine = new GameStateMachine();
            Assert.AreEqual(GameMode.Boot, machine.Current);
            Assert.IsFalse(machine.TryTransition(GameMode.AtTable));
            Assert.IsTrue(machine.TryTransition(GameMode.Exploring));
        }

        [Test]
        public void ExploringToTableAndBack()
        {
            var machine = new GameStateMachine();
            machine.TryTransition(GameMode.Exploring);
            Assert.IsTrue(machine.TryTransition(GameMode.AtTable));
            Assert.IsTrue(machine.TryTransition(GameMode.Exploring));
        }

        [Test]
        public void Resume_ReturnsToInterruptedMode()
        {
            var machine = new GameStateMachine();
            machine.TryTransition(GameMode.Exploring);
            machine.TryTransition(GameMode.AtTable);
            Assert.IsTrue(machine.TryPause());
            Assert.IsFalse(machine.TryTransition(GameMode.Exploring), "Pause must not leak into a different mode.");
            Assert.IsTrue(machine.TryResume());
            Assert.AreEqual(GameMode.AtTable, machine.Current);
        }

        [Test]
        public void RunEnded_IsTerminal()
        {
            var machine = new GameStateMachine();
            machine.TryTransition(GameMode.Exploring);
            Assert.IsTrue(machine.TryTransition(GameMode.RunEnded));
            Assert.IsFalse(machine.TryTransition(GameMode.Exploring));
            Assert.IsFalse(machine.TryPause());
        }

        [Test]
        public void SameModeTransition_IsRejected()
        {
            var machine = new GameStateMachine();
            machine.TryTransition(GameMode.Exploring);
            Assert.IsFalse(machine.TryTransition(GameMode.Exploring));
        }

        [Test]
        public void ModeChanged_ReportsPreviousAndNext()
        {
            var machine = new GameStateMachine();
            var log = new List<string>();
            machine.ModeChanged += (from, to) => log.Add(from + ">" + to);
            machine.TryTransition(GameMode.Exploring);
            machine.TryPause();
            machine.TryResume();
            CollectionAssert.AreEqual(new[] { "Boot>Exploring", "Exploring>Paused", "Paused>Exploring" }, log);
        }
    }
}
