using NUnit.Framework;
using OmsiMapEditor.OMSI;
using OmsiMapEditor.UI;
using UnityEngine.UIElements;

namespace OmsiMapEditor.Tests
{
    /// <summary>The inspector's number fields show three decimals but never change the stored value.</summary>
    public sealed class RoundedFieldValueTests
    {
        private static TextField Field(double value)
        {
            return new TextField { value = OmsiNumberFormat.Format(value) };
        }

        [Test]
        public void ALongNumberIsShownToThreeDecimals()
        {
            var field = Field(289.47708489123456);
            RoundedFieldValue.Attach(field);
            Assert.AreEqual("289.477", field.value);
            Assert.AreEqual("0.25", RoundedFieldValue.DisplayOf("0.249999996"));
            Assert.AreEqual("-12.346", RoundedFieldValue.DisplayOf("-12.3456"));
        }

        [Test]
        public void AShortNumberAndTextThatIsNoNumberStayAsTheyAre()
        {
            var field = Field(12.5);
            RoundedFieldValue.Attach(field);
            Assert.AreEqual("12.5", field.value);
            Assert.IsFalse(RoundedFieldValue.TryOriginal(field, out _));
            Assert.AreEqual("abc", RoundedFieldValue.DisplayOf("abc"));
        }

        [Test]
        public void AFieldLeftAsShownGivesBackTheFullValue()
        {
            var field = Field(289.47708489123456);
            RoundedFieldValue.Attach(field);

            Assert.IsTrue(RoundedFieldValue.TryOriginal(field, out var full));
            Assert.AreEqual(289.47708489123456, double.Parse(full, System.Globalization.CultureInfo.InvariantCulture), 1e-12);
            Assert.AreEqual(OmsiNumberFormat.Format(289.47708489123456), full);
        }

        [Test]
        public void AnEditedFieldIsNotReplacedByTheOriginal()
        {
            var field = Field(289.47708489123456);
            RoundedFieldValue.Attach(field);
            field.SetValueWithoutNotify("300");

            Assert.IsFalse(RoundedFieldValue.TryOriginal(field, out _));
        }

        [Test]
        public void ATinyValueRoundingToZeroStillKeepsItsOriginal()
        {
            var field = Field(1.5E-6);
            RoundedFieldValue.Attach(field);

            Assert.AreEqual("0", field.value);
            Assert.IsTrue(RoundedFieldValue.TryOriginal(field, out var full));
            Assert.AreEqual(OmsiNumberFormat.Format(1.5E-6), full);
        }
    }
}
