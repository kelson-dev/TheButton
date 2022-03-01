using System.Text;
using System.Threading;

namespace SelfDestructChannel
{
    [JsonConverter(typeof(SnowflakeJsonConverter))]
    public readonly struct Snowflake : IComparable<Snowflake>
    {
        private readonly long value;

        public Snowflake(long value) => this.value = value;
        public Snowflake(ulong value) => this.value = (long)value;

        private const long DiscordEpoch = 1420070400000L;
        public DateTimeOffset ToTimestamp() => DateTimeOffset.FromUnixTimeMilliseconds((value >> 22) + DiscordEpoch);

        static volatile int _increment = 0;
        public static Snowflake New()
        {
            long timestamp = (DateTimeOffset.UnixEpoch.ToUnixTimeMilliseconds() + DiscordEpoch) << 22;
            long workerId = ((long)Thread.CurrentThread.ManagedThreadId & 0b11111) << 17;
            long processId = ((long)Environment.ProcessId & 0b11111) << 12;
            long increment = (_increment++) & 0xFFF;
            return timestamp | workerId | processId | increment;
        }

        public ushort Increment => (ushort)(value & 0xFFF);

        public static bool operator ==(Snowflake a, Snowflake b) => a.value == b.value;
        public static bool operator !=(Snowflake a, Snowflake b) => a.value != b.value;

        public static implicit operator ulong(Snowflake id) => (ulong)id.value;
        public static implicit operator long(Snowflake id) => id.value;
        public static implicit operator Snowflake(ulong value) => new(value);
        public static implicit operator Snowflake(long value) => new(value);

        public override string ToString() => ((ulong)value).ToString();

        public int CompareTo(Snowflake other) => value.CompareTo(other);

        public override bool Equals(object? obj) => obj switch
        {
            null => value == 0,
            Snowflake snowflake => this == snowflake,
            long l => value == l,
            ulong u => (ulong)value == u,
            _ => false
        };


        public override int GetHashCode() => value.GetHashCode();

        public static bool operator <(Snowflake left, Snowflake right) => left.CompareTo(right) < 0;
        public static bool operator <=(Snowflake left, Snowflake right) => left.CompareTo(right) <= 0;
        public static bool operator >(Snowflake left, Snowflake right) => left.CompareTo(right) > 0;
        public static bool operator >=(Snowflake left, Snowflake right) => left.CompareTo(right) >= 0;
    }

    public static class SnowflakeGeneration
    {
        public static Snowflake ToSnowflakeId(this IEmote emote)
        {
            if (emote is Emote custom)
                return custom.Id;
            else
            {
                string emoji = emote.Name;
                // This algorithm generates a unique discord snowflake ID for every Unicode 13.1 emoji
                // with a timestamp value between Jan 1 2015 and Feb 15 2015: before discord launched
                // in May 2015.
                emoji = emoji.Length > 4 ? emoji[..4] : emoji;
                Span<byte> buffer = stackalloc byte[32];
                int length = Encoding.UTF8.GetBytes(emoji, buffer);
                var data = buffer[..length];
                ulong asLong =
                      (((ulong)buffer[0] & 0b111111) << (8 * 6))
                    | ((ulong)(buffer[1] ^ buffer[5] ^ buffer[0] ^ buffer[4]) << (8 * 5))
                    | ((ulong)(buffer[2] ^ buffer[6]) << (8 * 4))
                    | ((ulong)(buffer[3] ^ buffer[7]) << (8 * 3))
                    | ((ulong)(data[^1] ^ data[^2]) << 16) | data[^2] | data[^1];
                return new(asLong);
            }
        }
    }

    public class SnowflakeJsonConverter : JsonConverter<Snowflake>
    {
        public override Snowflake Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => reader.GetUInt64();
        public override void Write(Utf8JsonWriter writer, Snowflake value, JsonSerializerOptions options) => writer.WriteNumberValue((ulong)value);
    }
}
