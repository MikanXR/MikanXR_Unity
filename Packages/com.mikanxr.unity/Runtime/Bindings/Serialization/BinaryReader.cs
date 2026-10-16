using System;

namespace MikanXR
{
	public class BinaryReader
	{
		private byte[] _readBuffer = null;
		private int _readBufferIndex = 0;

		public BinaryReader(byte[] soruceBuffer)
		{
			_readBuffer= soruceBuffer;
			_readBufferIndex = 0;
		}

		public bool ReadBoolean()
		{
			return ReadByte() > 0;
		}

		public sbyte ReadSByte()
		{
			// Intentionally disable overflow checking for byte -> sbyte
			// See https://simsekahmett.medium.com/understanding-and-using-the-unchecked-keyword-in-c-e6489d6b73aa
			sbyte value = unchecked((sbyte)_readBuffer[_readBufferIndex]);
			_readBufferIndex++;

			return value;
		}

		public byte ReadByte()
		{
			byte value = _readBuffer[_readBufferIndex];
			_readBufferIndex++;

			return value;
		}

		public short ReadInt16()
		{
			short value = BitConverter.ToInt16(_readBuffer, _readBufferIndex);
			_readBufferIndex += sizeof(short);

			return value;
		}

		public ushort ReadUInt16()
		{
			ushort value = BitConverter.ToUInt16(_readBuffer, _readBufferIndex);
			_readBufferIndex += sizeof(ushort);

			return value;
		}

		public int ReadInt32()
		{
			int value = BitConverter.ToInt32(_readBuffer, _readBufferIndex);
			_readBufferIndex += sizeof(int);

			return value;
		}

		public uint ReadUInt32()
		{
			uint value = BitConverter.ToUInt32(_readBuffer, _readBufferIndex);
			_readBufferIndex += sizeof(uint);

			return value;
		}

		public long ReadInt64()
		{
			long value = BitConverter.ToInt64(_readBuffer, _readBufferIndex);
			_readBufferIndex += sizeof(long);

			return value;
		}

		public ulong ReadUInt64()
		{
			ulong value = BitConverter.ToUInt64(_readBuffer, _readBufferIndex);
			_readBufferIndex += sizeof(ulong);

			return value;
		}

		public float ReadSingle()
		{
			float value= BitConverter.ToSingle(_readBuffer, _readBufferIndex);
			_readBufferIndex += sizeof(float);

			return value;
		}

		public double ReadDouble()
		{
			double value= BitConverter.ToDouble(_readBuffer, _readBufferIndex);
			_readBufferIndex += sizeof(double);

			return value;
		}

		public string ReadUTF8String()
		{
			Int32 byteCount = ReadInt32();
			string utf8String= "";
			if (byteCount > 0)
			{
				utf8String= System.Text.Encoding.UTF8.GetString(_readBuffer, _readBufferIndex, byteCount);
				_readBufferIndex += byteCount;
			}

			return utf8String;
		}
	}
}