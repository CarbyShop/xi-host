using XI.Host.Login;
using System.Data;
using System.Net;

namespace XI.Host.Tests
{
    [TestClass]
    public class ExtensionTests
    {
        private static DataRow CreateWithKeyValuePair(KeyValuePair<string, object> keyValuePair)
        {
            var dataTable = new DataTable();
            dataTable.Columns.Add(keyValuePair.Key);

            var dataRow = dataTable.NewRow();
            dataRow[keyValuePair.Key] = keyValuePair.Value;

            return dataRow;
        }

        [TestMethod]
        public void ZoneIpAsUInt32_Should_ReturnCorrectValue()
        {
            // Arrange
            var keyValuePair = new KeyValuePair<string, object>("ZoneIp", "192.168.0.1");
            var dataRow = CreateWithKeyValuePair(keyValuePair);
            uint expectedValue = 16820416;

            // Act
            uint actualValue = dataRow.ZoneIpAsUInt32();

            // Assert
            Assert.AreEqual(expectedValue, actualValue);
        }
    }
}