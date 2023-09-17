using MongoDB.Bson;
using MongoDB.Driver;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace MySmartHouse
{
    public partial class History : Window
    {
        private MongoClient client;
        private IMongoCollection<BsonDocument> greenhouse;

        public History()
        {
            InitializeComponent();

            const string connectionUrl = "mongodb://localhost:27017";
            client = new MongoClient(connectionUrl);
            IMongoDatabase database = client.GetDatabase("greenhouse");
            greenhouse = database.GetCollection<BsonDocument>("greenhouse");
        }

        private async void OK_Button_Click(object sender, RoutedEventArgs e)
        {
            if (Sensors_ComboBox.SelectedItem != null && Start_Date.SelectedDate != null && End_Date.SelectedDate != null)
            {
                string selectedSensor = ((ComboBoxItem)Sensors_ComboBox.SelectedItem).Content.ToString();
                DateTime startDate = Start_Date.SelectedDate.Value;
                DateTime endDate = End_Date.SelectedDate.Value;

                var filter = Builders<BsonDocument>.Filter.And(
                    Builders<BsonDocument>.Filter.Gte("timestamp", startDate),
                    Builders<BsonDocument>.Filter.Lte("timestamp", endDate.AddDays(1).AddMilliseconds(-1))
                );

                var projection = Builders<BsonDocument>.Projection.Include("timestamp").Include($"sensor_values.{selectedSensor}");
                var sortedHistory = await greenhouse.Find(filter).Project(projection).ToListAsync();
                Console.WriteLine(sortedHistory.Count.ToString());
                Console.WriteLine(sortedHistory.ToArray().ToString());
                History_List.ItemsSource = sortedHistory.Select(doc => new
                {
                    Date = doc["timestamp"].ToLocalTime(),
                    Value = doc["sensor_values"][selectedSensor].ToInt32()
                });
            }
            else
            {
                Console.WriteLine("Пожалуйста, выберите дату и предмет.");
            }
        }
    }
}
