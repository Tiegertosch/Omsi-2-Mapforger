using System.Text;
using NUnit.Framework;
using OmsiMapEditor.OMSI;

namespace OmsiMapEditor.Tests
{
    public sealed class OmsiTextFileTests
    {
        [Test]
        public void DecodesUtf16LittleEndianWithByteOrderMark()
        {
            var bytes = Combine(new byte[] { 0xFF, 0xFE }, Encoding.Unicode.GetBytes("[name]\r\nStadtückheim"));

            Assert.AreEqual("[name]\r\nStadtückheim", OmsiTextFile.Decode(bytes));
        }

        [Test]
        public void DecodesUtf8WithoutByteOrderMark()
        {
            Assert.AreEqual("Grüße", OmsiTextFile.Decode(Encoding.UTF8.GetBytes("Grüße")));
        }

        [Test]
        public void StripsUtf8ByteOrderMark()
        {
            var bytes = Combine(new byte[] { 0xEF, 0xBB, 0xBF }, Encoding.UTF8.GetBytes("[map]"));

            Assert.AreEqual("[map]", OmsiTextFile.Decode(bytes));
        }

        [Test]
        public void FallsBackToWindows1252ForInvalidUtf8()
        {
            // "Straße 5€" in Windows-1252: ß = 0xDF, € = 0x80.
            var bytes = new byte[] { 0x53, 0x74, 0x72, 0x61, 0xDF, 0x65, 0x20, 0x35, 0x80 };

            Assert.AreEqual("Straße 5€", OmsiTextFile.Decode(bytes));
        }

        private static byte[] Combine(byte[] first, byte[] second)
        {
            var result = new byte[first.Length + second.Length];
            first.CopyTo(result, 0);
            second.CopyTo(result, first.Length);
            return result;
        }
    }
}
