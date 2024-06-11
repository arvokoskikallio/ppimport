using Dapper;
using System.Collections;
using System.Data.SqlClient;
using System.Globalization;

namespace PPImport
{
    class Program
    {
        private static string[] contents = File.ReadAllText(@"C:\Users\arvok\ppimport\config.txt").Split("\r\n");
        private static string _connectionString = contents[0];
        private static DateTime minDate = DateTime.Parse("2008-04-09");

        static async Task Main(string[] args)
        {
            List<MarioKartData> marioKartData = new List<MarioKartData>();
            using (StreamReader r = new StreamReader("C:\\Users\\arvok\\ppimport\\mkwpp times.json"))
            {
                string json = r.ReadToEnd();
                marioKartData = Newtonsoft.Json.JsonConvert.DeserializeObject<List<MarioKartData>>(json);
            }

            foreach (var data in marioKartData)
            {
                // Map to List<Player> and List<Time>
                var player = MapPlayer(data);
                var times = MapTimes(data);
                var threeLapTimes = times.Where(t => !t.Flap);
                var flapTimes = times.Where(t => t.Flap);
                List<int> playersWithTies = new();
                var playerIsUnique = true;

                //loop through all 3lap times to find players who tie them
                foreach(var time in threeLapTimes)
                {
                    playersWithTies.AddRange(await GetPlayerTiesByTime(time));
                }

                //find most commonly found player to have ties, as well as the amount of ties that player has
                var playerWithMostTies = FindMostCommonInteger(playersWithTies);

                //assert that if a player's 3lap timesheet have more than 1/3 percentage of ties, the player is not unique (more than 1/2 if player has less than 3 times)
                if((threeLapTimes.Count() >= 3 && playerWithMostTies.TieCount > threeLapTimes.Count() * 0.34) || (threeLapTimes.Count() < 3 && playerWithMostTies.TieCount > threeLapTimes.Count() * 0.51))
                {
                    var existingPlayer = await GetPlayer(playerWithMostTies.PlayerId);
                    Console.WriteLine("Duplicate found (" + playerWithMostTies.TieCount + "/" + threeLapTimes.Count() + " 3lap ties) - " + player.Name + " = " + existingPlayer.Name + " - Importing only flaps and potential faster times");
                    playerIsUnique = false;
                }

                //if player is unique, import the player, as well as their times
                if(playerIsUnique)
                {
                    Console.WriteLine("New player found - " + player.Name + " - Importing all times");
                    
                    var playerId = await PushPlayer(player);

                    foreach (var time in times)
                    {
                        time.PlayerId = playerId;
                        PushTime(time);
                    }
                }
                else
                {
                    var playerId = playerWithMostTies.PlayerId;
                    var existingPlayer = await GetPlayer(playerWithMostTies.PlayerId);

                    //add potential PP player info to the existing player
                    UpdatePlayerInfo(playerId, existingPlayer);

                    //if player is not unique, import all the flaps to the existing player's timesheet (there are no flaps in the MKL data)
                    foreach (var time in flapTimes)
                    {
                        time.PlayerId = playerId;
                        PushTime(time);
                    }

                    var existingThreeLapTimes = await GetThreeLapTimesByPlayer(playerId);

                    //loop through existing 3lap times, if the PP dump has a faster time than existing MKL time, obsolete the old time and keep the faster one
                    foreach(var existingTime in existingThreeLapTimes)
                    {
                        var newTime = threeLapTimes.Where(t => existingTime.Glitch == t.Glitch && existingTime.Track == t.Track).FirstOrDefault();

                        if(newTime != null && existingTime.RunTime > newTime.RunTime)
                        {
                            Obsolete(existingTime.Id);
                            newTime.PlayerId = playerId;
                            PushTime(newTime);
                        }
                    }
                }
            }
        }

        static Country GetCountryByLongName(string fullName)
        {
            foreach (CultureInfo ci in CultureInfo.GetCultures(CultureTypes.SpecificCultures))
            {
                RegionInfo region = new RegionInfo(ci.Name);
                if (region.EnglishName == fullName)
                {
                    return Enum.Parse<Country>(region.TwoLetterISORegionName);
                }
            }

            if(fullName == "UK") {
                return Country.GB;
            }

            if(fullName == "USA") {
                return Country.US;
            }

            if(fullName == "Ivory Coast") {
                return Country.CI;
            }

            if(fullName == "Dominican Rep") {
                return Country.DO;
            }

            if(fullName == "UAE") {
                return Country.AE;
            }

            if(fullName == "Bosnia") {
                return Country.BA;
            }

            if(fullName == "South Korea") {
                return Country.KR;
            }


            return Country.Unknown;
        }

        static Player MapPlayer(MarioKartData data)
        {
            return new Player
                {
                    Name = data.Info.Name,
                    Country = GetCountryByLongName(data.Info.Country),
                    Town = data.Info.Town,
                    OtherInfo = data.Info.OtherInfo.Replace(" (", ""),
                    PPProofStatus = data.Info.ProofStatus
                };
        }

        static List<Time> MapTimes(MarioKartData data)
        {
            List<string> tracks = new List<string>
            {
                "Luigi Circuit",
                "Moo Moo Meadows",
                "Mushroom Gorge",
                "Toad's Factory",
                "Mario Circuit",
                "Coconut Mall",
                "DK's Snowboard Cross",
                "Wario's Gold Mine",
                "Daisy Circuit",
                "Koopa Cape",
                "Maple Treeway",
                "Grumble Volcano",
                "Dry Dry Ruins",
                "Moonview Highway",
                "Bowser's Castle",
                "Rainbow Road",
                "GCN Peach Beach",
                "DS Yoshi Falls",
                "SNES Ghost Valley 2",
                "N64 Mario Raceway",
                "N64 Sherbet Land",
                "GBA Shy Guy Beach",
                "DS Delfino Square",
                "GCN Waluigi Stadium",
                "DS Desert Hills",
                "GBA Bowser Castle 3",
                "N64 DK's Jungle Parkway",
                "GCN Mario Circuit",
                "SNES Mario Circuit 3",
                "DS Peach Gardens",
                "GCN DK Mountain",
                "N64 Bowser's Castle",
            };

            var times = new List<Time>();

            foreach (var timeEntry in data.Times)
            {
                times.Add(new Time
                {
                    Date = DateTime.Parse(timeEntry.Date),
                    Track = Array.IndexOf(tracks.ToArray(), tracks.First(t => t == timeEntry.Track)),
                    Glitch = timeEntry.Glitch,
                    Flap = timeEntry.Flap,
                    RunTime = timeEntry.m*60*1000 + timeEntry.s*1000 + timeEntry.ms,
                    Link = timeEntry.Video
                });
            }
            return times;
        }

        static async Task<int> PushPlayer(Player player)
        {
            string sqlQuery = "INSERT INTO Players (Name, Country, Town, OtherInfo, PPProofStatus)" +
            "VALUES (@Name, @Country, @Town, @OtherInfo, @PPProofStatus); SELECT CAST(SCOPE_IDENTITY() as int)";

            using var connection = new SqlConnection(_connectionString);
            return await connection.QuerySingleAsync<int>(sqlQuery, player);
        }

        private static async void PushTime(Time time)
        {
            if(time.Date < minDate) {
                time.Date = null;
            }

            string sqlQuery = "INSERT INTO Times (PlayerId, Date, Track, Glitch, Flap, RunTime, Link, Obsoleted)" +
            "VALUES (@PlayerId, @Date, @Track, @Glitch, @Flap, @RunTime, @Link, 0)";


            using var connection = new SqlConnection(_connectionString);
            await connection.ExecuteAsync(sqlQuery, time);
        }

        private static async Task<IEnumerable<int>> GetPlayerTiesByTime(Time time)
        {
            string sqlQuery = "SELECT PlayerId FROM Times WHERE RunTime = @RunTime AND Glitch = @Glitch AND Flap = @Flap AND Track = @Track";

            using var connection = new SqlConnection(_connectionString);
            return await connection.QueryAsync<int>(sqlQuery, time);
        }
        
        private static async Task<IEnumerable<Time>> GetThreeLapTimesByPlayer(int playerId)
        {
            string sqlQuery = "SELECT * FROM Times WHERE PlayerId = @PlayerId AND Flap = 0";

            using var connection = new SqlConnection(_connectionString);
            return await connection.QueryAsync<Time>(sqlQuery, new { PlayerId = playerId });
        }
        
        private static async Task<Player> GetPlayer(int id)
        {
            string sqlQuery = "SELECT * FROM Players WHERE Id = @Id";

            using var connection = new SqlConnection(_connectionString);
            return await connection.QueryFirstOrDefaultAsync<Player>(sqlQuery, new { Id = id });
        }

        private static async void Obsolete(int id)
        {
            string sqlQuery = "UPDATE Times SET Obsoleted = 1 WHERE Id = @Id";

            using var connection = new SqlConnection(_connectionString);
            await connection.ExecuteAsync(sqlQuery, new { Id = id });
        }

        private static async void UpdatePlayerInfo(int id, Player player)
        {
            player.Id = id;
            string sqlQuery = "UPDATE Players SET Town = @Town, OtherInfo = @OtherInfo, PPProofStatus = @PPProofStatus WHERE Id = @Id";

            using var connection = new SqlConnection(_connectionString);
            await connection.ExecuteAsync(sqlQuery, player);
        }

        private static PlayerWithTies FindMostCommonInteger(List<int> list)
        {
            Dictionary<int, int> counts = new();

            // Count the occurrences of each integer in the list
            foreach (var num in list)
            {
                if (counts.ContainsKey(num))
                {
                    counts[num]++;
                }
                else
                {
                    counts[num] = 1;
                }
            }

            if(counts.Keys.Count == 0)
            {
                return new PlayerWithTies(0, 0);
            }

            // Find the integer with the maximum count
            int mostCommon = counts.Keys.First();
            int maxCount = counts[mostCommon];

            foreach (var kvp in counts)
            {
                if (kvp.Value > maxCount)
                {
                    mostCommon = kvp.Key;
                    maxCount = kvp.Value;
                }
            }

            return new PlayerWithTies(mostCommon, maxCount);
        }
    }

    public class TimeEntry
    {
        public string Date { get; set; }
        public string Name { get; set; }
        public string Track { get; set; }
        public int m { get; set; }
        public int s { get; set; }
        public int ms { get; set; }
        public bool Flap { get; set; }
        public bool Glitch { get; set; }
        public string Video { get; set; }
    }

    public class PlayerInfo
    {
        public string Name { get; set; }
        public string Country { get; set; }
        public string Town { get; set; }
        public string OtherInfo { get; set; }
        public string ProofStatus { get; set; }
    }

    public class MarioKartData
    {
        public PlayerInfo Info { get; set; }
        public List<TimeEntry> Times { get; set; }
    }    
    
    public class PlayerWithTies
    {
        public PlayerWithTies(int playerId, int tieCount)
        {
            PlayerId = playerId;
            TieCount = tieCount;
        }

        public int PlayerId { get; set; }
        public int TieCount { get; set; }
    }
}