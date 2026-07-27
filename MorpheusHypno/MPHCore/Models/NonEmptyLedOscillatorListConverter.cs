using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MPHCore.Models
{
    /// <summary>
    /// JSON converter that serializes a list of <see cref="Oscillator"/> while omitting
    /// any oscillator entries whose <see cref="Oscillator.LEDs"/> collection is null or empty.
    ///
    /// This ensures that oscillators with no selected LEDs are not written to the JSON payload,
    /// without mutating the in-memory model. Deserialization behaves normally and accepts
    /// any list of oscillators (including those with empty LED lists if they exist in files
    /// produced by other tools or older versions).
    /// </summary>
    public sealed class NonEmptyLedOscillatorListConverter : JsonConverter<List<Oscillator>>
    {
        /// <summary>
        /// Reads a JSON array of oscillators and deserializes it into a <see cref="List{Oscillator}"/>.
        /// This method does not filter items, keeping behavior fully compatible with existing files.
        /// </summary>
        /// <param name="reader">The JSON reader.</param>
        /// <param name="objectType">The target type.</param>
        /// <param name="existingValue">The existing value.</param>
        /// <param name="hasExistingValue">Not used.</param>
        /// <param name="serializer">The JSON serializer.</param>
        /// <returns>The deserialized list of oscillators.</returns>
        public override List<Oscillator>? ReadJson(JsonReader reader, Type objectType, List<Oscillator>? existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null)
            {
                return null;
            }

            var array = JArray.Load(reader);
            // Use default deserialization for compatibility
            var list = array.ToObject<List<Oscillator>>(serializer) ?? new List<Oscillator>();
            return list;
        }

        /// <summary>
        /// Writes the list of oscillators filtering out entries with no LEDs (<c>LEDs == null</c> or <c>LEDs.Count == 0</c>).
        /// </summary>
        /// <param name="writer">The JSON writer.</param>
        /// <param name="value">The list of oscillators.</param>
        /// <param name="serializer">The JSON serializer.</param>
        public override void WriteJson(JsonWriter writer, List<Oscillator>? value, JsonSerializer serializer)
        {
            writer.WriteStartArray();

            if (value != null)
            {
                foreach (var osc in value)
                {
                    // Keep only oscillators that reference at least one LED
                    if (osc?.LEDs != null && osc.LEDs.Count > 0)
                    {
                        serializer.Serialize(writer, osc);
                    }
                }
            }

            writer.WriteEndArray();
        }
    }
}
