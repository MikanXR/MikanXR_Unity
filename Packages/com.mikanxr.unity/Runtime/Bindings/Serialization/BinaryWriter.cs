using System;

namespace MikanXR
{
	public class BinaryWriter
	{
		private static readonly int DefaultCapacity = 1024;

		private int _bytesWritten;
		private int _capacity;
		private byte[] _writeBuffer;

		public BinaryWriter()
		{
			_bytesWritten= 0;
			_capacity= DefaultCapacity;
			_writeBuffer= new byte[_capacity];
		}

		private void EnsureCapacity(int additionalBytes)
		{
			if (_bytesWritten + additionalBytes > _capacity)
			{
				_capacity= Math.Max(_capacity * 2, _bytesWritten + additionalBytes);
				Array.Resize(ref _writeBuffer, _capacity);
			}
		}

		public void Write(bool value)
		{
			Write(value ? (byte)1 : (byte)0);
		}

		public void Write(sbyte value)
		{
			EnsureCapacity(sizeof(sbyte));
			_writeBuffer[_bytesWritten]= unchecked((byte)value);
			_bytesWritten += sizeof(byte);
		}

		public void Write(byte value)
		{
			EnsureCapacity(sizeof(byte));
			_writeBuffer[_bytesWritten]= value;
			_bytesWritten += sizeof(byte);
		}

		public void Write(short value)
		{
			EnsureCapacity(sizeof(short));
			byte[] valueBytes = BitConverter.GetBytes(value);
			valueBytes.CopyTo(_writeBuffer, _bytesWritten);
			_bytesWritten += sizeof(short);
		}

		public void Write(ushort value)
		{
			EnsureCapacity(sizeof(ushort));
			byte[] valueBytes = BitConverter.GetBytes(value);
			valueBytes.CopyTo(_writeBuffer, _bytesWritten);
			_bytesWritten += sizeof(ushort);
		}

		public void Write(int value)
		{
			EnsureCapacity(sizeof(int));
			byte[] valueBytes = BitConverter.GetBytes(value);
			valueBytes.CopyTo(_writeBuffer, _bytesWritten);
			_bytesWritten += sizeof(int);
		}

		public void Write(uint value)
		{
			EnsureCapacity(sizeof(uint));
			byte[] valueBytes = BitConverter.GetBytes(value);
			valueBytes.CopyTo(_writeBuffer, _bytesWritten);
			_bytesWritten += sizeof(uint);
		}

		public void Write(long value)
		{
			EnsureCapacity(sizeof(long));
			byte[] valueBytes = BitConverter.GetBytes(value);
			valueBytes.CopyTo(_writeBuffer, _bytesWritten);
			_bytesWritten += sizeof(long);
		}

		public void Write(ulong value)
		{
			EnsureCapacity(sizeof(ulong));
			byte[] valueBytes = BitConverter.GetBytes(value);
			valueBytes.CopyTo(_writeBuffer, _bytesWritten);
			_bytesWritten += sizeof(ulong);
		}

		public void Write(float value)
		{
			EnsureCapacity(sizeof(float));
			byte[] floatBytes = BitConverter.GetBytes(value);
			floatBytes.CopyTo(_writeBuffer, _bytesWritten);
			_bytesWritten += sizeof(float);
		}

		public void Write(double value)
		{
			EnsureCapacity(sizeof(double));
			byte[] doubleBytes = BitConverter.GetBytes(value);
			doubleBytes.CopyTo(_writeBuffer, _bytesWritten);
			_bytesWritten += sizeof(double);
		}

		public void WriteUTF8String(string unicodeString)
		{
			byte[] utf8Bytes = System.Text.Encoding.UTF8.GetBytes(unicodeString);

			Int32 stringLength = utf8Bytes.Length;
			Write(stringLength);

			if (stringLength > 0)
			{
				Write(utf8Bytes);
			}
		}

		public void Write(byte[] value)
		{
			EnsureCapacity(value.Length);
			value.CopyTo(_writeBuffer, _bytesWritten);
			_bytesWritten += value.Length;
		}

		public byte[] ToArray()
		{
			if (_writeBuffer.Length != _bytesWritten)
			{
				Array.Resize(ref _writeBuffer, _bytesWritten);
			}

			return _writeBuffer;
		}
	}
}