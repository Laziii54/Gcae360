using Microsoft.Data.Sqlite;

namespace CarScanner.Api.Services;

public sealed class WebhookEventStore
{
    private readonly string _connectionString;
    private readonly SemaphoreSlim _gate = new(1, 1);
    public WebhookEventStore(
        IHostEnvironment env,
        IConfiguration configuration)
    {
        var path = BackendDataPath.ResolveDatabasePath(
            env,
            configuration);
        _connectionString =
            BackendDataPath.CreateConnectionString(path);
        using var c=new SqliteConnection(_connectionString); c.Open(); using var cmd=c.CreateCommand();
        cmd.CommandText="CREATE TABLE IF NOT EXISTS WebhookEvents(EventId TEXT PRIMARY KEY, ReceivedAt TEXT NOT NULL);"; cmd.ExecuteNonQuery();
        MigrateLegacyJson(env);
    }
    public async Task<bool> TryRegisterAsync(string eventId, CancellationToken token)
    {
        if(string.IsNullOrWhiteSpace(eventId)) return false;
        await _gate.WaitAsync(token);
        try{using var c=new SqliteConnection(_connectionString); c.Open(); using var cmd=c.CreateCommand(); cmd.CommandText="INSERT OR IGNORE INTO WebhookEvents(EventId,ReceivedAt) VALUES($id,$at);"; cmd.Parameters.AddWithValue("$id",eventId); cmd.Parameters.AddWithValue("$at",DateTimeOffset.UtcNow.ToString("O")); return cmd.ExecuteNonQuery()==1;}
        finally{_gate.Release();}
    }    private void MigrateLegacyJson(IHostEnvironment env)
    {
        try
        {
            var legacy=Path.Combine(env.ContentRootPath,"data","webhook-events.json"); if(!File.Exists(legacy)) return;
            var data=System.Text.Json.JsonSerializer.Deserialize<string[]>(File.ReadAllText(legacy)); if(data==null) return;
            using var c=new SqliteConnection(_connectionString); c.Open();
            foreach(var id in data.Where(x=>!string.IsNullOrWhiteSpace(x))){using var cmd=c.CreateCommand();cmd.CommandText="INSERT OR IGNORE INTO WebhookEvents(EventId,ReceivedAt) VALUES($id,$at)";cmd.Parameters.AddWithValue("$id",id);cmd.Parameters.AddWithValue("$at",DateTimeOffset.UtcNow.ToString("O"));cmd.ExecuteNonQuery();}
            File.Move(legacy,legacy+".migrated",true);
        } catch { }
    }

}
