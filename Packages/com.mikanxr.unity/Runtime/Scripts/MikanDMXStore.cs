using System;
using System.Collections.Generic;

namespace MikanXR.Unity
{
	// The latest DMX channel bytes per universe, as the editor streams them to a subscribed
	// client. The fixtures read their channels here whenever a universe changes.
	public class MikanDMXStore
	{
		public const int k_universeChannelCount = 512;

		private readonly Dictionary<int, byte[]> _universes = new Dictionary<int, byte[]>();

		public event Action Changed;

		public void Apply(MikanDMXData dmxData)
		{
			if (dmxData?.universes == null)
			{
				return;
			}

			foreach (MikanUniverseDMXData universeData in dmxData.universes)
			{
				if (universeData == null)
				{
					continue;
				}

				int universeId = universeData.dmx_universe_id;
				if (!_universes.TryGetValue(universeId, out byte[] channels))
				{
					channels = new byte[k_universeChannelCount];
					_universes[universeId] = channels;
				}

				Decode(universeData, channels);
			}

			Changed?.Invoke();
		}

		// A channel by its DMX number, 1 to 512; anything else, or an unseen universe, reads 0
		public byte ReadChannel(int universeId, int channel)
		{
			if (channel < 1 || channel > k_universeChannelCount ||
				!_universes.TryGetValue(universeId, out byte[] channels))
			{
				return 0;
			}

			return channels[channel - 1];
		}

		public void Clear()
		{
			_universes.Clear();
			Changed?.Invoke();
		}

		// The wire form is either the raw channel bytes or [run length, value] pairs
		private static void Decode(MikanUniverseDMXData universeData, byte[] channels)
		{
			Array.Clear(channels, 0, channels.Length);

			List<byte> encoded = universeData.buffer_data;
			if (encoded == null)
			{
				return;
			}

			if (universeData.buffer_format == MikanDMXBufferFormat.DMXUncompressed)
			{
				int count = Math.Min(encoded.Count, channels.Length);
				for (int index = 0; index < count; ++index)
				{
					channels[index] = encoded[index];
				}
				return;
			}

			int outIndex = 0;
			for (int pair = 0; pair + 1 < encoded.Count && outIndex < channels.Length; pair += 2)
			{
				int runLength = encoded[pair];
				byte value = encoded[pair + 1];
				for (int run = 0; run < runLength && outIndex < channels.Length; ++run)
				{
					channels[outIndex++] = value;
				}
			}
		}
	}
}
