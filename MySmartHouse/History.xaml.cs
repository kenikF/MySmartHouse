using MongoDB.Bson;
using MongoDB.Driver;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace MySmartHouse
{
    public partial class History : Window
    {
        private const string ConnectionUrl = "mongodb://localhost:27017";
        private const string DatabaseName = "greenhouse";
        private const string CollectionName = "history";

        private IMongoCollection<BsonDocument> historyCollection;

        public History()
        {
            InitializeComponent();
            InitializeMongoDB();
        }

        private void InitializeMongoDB()
        {
            var client = new MongoClient(ConnectionUrl);
            var database = client.GetDatabase(DatabaseName);
            historyCollection = database.GetCollection<BsonDocument>(CollectionName);
        }

        private async Task<List<HistoryData>> GetHistoryData(DateTime startDate, DateTime endDate)
        {
            var filter = Builders<BsonDocument>.Filter.And(
                Builders<BsonDocument>.Filter.Gte("timestamp", startDate),
                Builders<BsonDocument>.Filter.Lte("timestamp", endDate));

            var projection = Builders<BsonDocument>.Projection
                .Include("timestamp")
                .Include("sensor_values.temp_internal_air")
                .Include("sensor_values.humidity_air");

            var historyDocuments = await historyCollection.Find(filter).Project(projection).ToListAsync();

            var historyDataList = new List<HistoryData>();

            foreach (var document in historyDocuments)
            {
                var historyData = new HistoryData
                {
                    Timestamp = document["timestamp"].ToUniversalTime(),
                    Temperature = document["sensor_values"]["temp_internal_air"].AsDouble,
                    Humidity = document["sensor_values"]["humidity_air"].AsDouble
                    // Добавьте другие необходимые свойства
                };

                historyDataList.Add(historyData);
            }

            return historyDataList;
        }

        private async void OK_Button_Click(object sender, RoutedEventArgs e)
        {
            if (Sensors_ComboBox.SelectedItem != null && Start_Date.SelectedDate != null && End_Date.SelectedDate != null)
            {
                string selectedSensor = ((ComboBoxItem)Sensors_ComboBox.SelectedItem).Content.ToString();
                DateTime startDate = Start_Date.SelectedDate.Value;
                DateTime endDate = End_Date.SelectedDate.Value;

                var historyData = await GetHistoryData(startDate, endDate);

                // Здесь вы можете привязать полученные данные к вашему DataGrid
                History_List.ItemsSource = historyData;
            }
            else
            {
                Console.WriteLine("Пожалуйста, выберите дату и предмет.");
            }
        }
    }
}

public class HistoryData
{
    public DateTime Timestamp { get; set; }
    public double Temperature { get; set; }
    public double Humidity { get; set; }
    // Добавьте другие необходимые свойства
}
