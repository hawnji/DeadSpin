using System.Collections.Generic;
using DeadSpin.Core.GameState;
using NUnit.Framework;

namespace DeadSpin.Tests
{
    public class GameStateMachineTests
    {
        [Test]
        public void StartsInBoot_AndOnlyAllowsTable()
        {
            var machine = new GameStateMachine();
            Assert.AreEqual(GameMode.Boot, machine.Current);
            Assert.IsFalse(machine.TryTransition(GameMode.Paused));
            Assert.IsFalse(machine.TryTransition(GameMode.RunEnded));
            Assert.IsTrue(machine.TryTransition(GameMode.AtTable));
        }

        [Test]
        public void PauseAndResume_ReturnToTable()
        {
            var machine = new GameStateMachine();
            machine.TryTransition(GameMode.AtTable);
            Assert.IsTrue(machine.TryPause());
            Assert.IsTrue(machine.IsPaused);
            Assert.IsTrue(machine.TryResume());
            Assert.AreEqual(GameMode.AtTable, machine.Current);
        }

        [Test]
        public void Resume_FailsWhenNotPaused()
        {
            var machine = new GameStateMachine();
            machine.TryTransition(GameMode.AtTable);
            Assert.IsFalse(machine.TryResume());
        }

        [Test]
        public void RunEnded_IsTerminal_FromTableOrPause()
        {
            var fromTable = new GameStateMachine();
            fromTable.TryTransition(GameMode.AtTable);
            Assert.IsTrue(fromTable.TryTransition(GameMode.RunEnded));
            Assert.IsFalse(fromTable.TryTransition(GameMode.AtTable));
            Assert.IsFalse(fromTable.TryPause());

            var fromPause = new GameStateMachine();
            fromPause.TryTransition(GameMode.AtTable);
            fromPause.TryPause();
            Assert.IsTrue(fromPause.TryTransition(GameMode.RunEnded));
        }

        [Test]
        public void SameModeTransition_IsRejected()
        {
            var machine = new GameStateMachine();
            machine.TryTransition(GameMode.AtTable);
            Assert.IsFalse(machine.TryTransition(GameMode.AtTable));
        }

        [Test]
        public void ModeChanged_ReportsPreviousAndNext()
        {
            var machine = new GameStateMachine();
            var log = new List<string>();
            machine.ModeChanged += (from, to) => log.Add(from + ">" + to);
            machine.TryTransition(GameMode.AtTable);
            machine.TryPause();
            machine.TryResume();
            CollectionAssert.AreEqual(new[] { "Boot>AtTable", "AtTable>Paused", "Paused>AtTable" }, log);
        }
    }
}
