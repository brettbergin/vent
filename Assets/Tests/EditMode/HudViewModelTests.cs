using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.UIElements;
using Vent.UI.Screens;

namespace Vent.Tests.EditMode
{
    /// <summary>The HUD document binds to this model; if it stops notifying, the HUD silently freezes.</summary>
    public sealed class HudViewModelTests
    {
        [Test]
        public void ChangingAPropertyRaisesPropertyChangedWithItsName()
        {
            var model = new HudViewModel();
            var raised = new List<string>();
            model.propertyChanged += (_, e) => raised.Add(e.propertyName);

            model.HealthText = "42";
            model.KillsText = "3 / 8";
            model.HealthFillWidth = Length.Percent(42f);

            CollectionAssert.AreEqual(new[] { nameof(HudViewModel.HealthText), nameof(HudViewModel.KillsText), nameof(HudViewModel.HealthFillWidth) }, raised);
        }

        [Test]
        public void SettingTheSameValueDoesNotNotify()
        {
            var model = new HudViewModel();
            int count = 0;
            model.propertyChanged += (_, _) => count++;

            model.HealthText = model.HealthText;
            model.VignetteOpacity = 0f;

            Assert.AreEqual(0, count);
        }
    }
}
