using System;
using System.Collections;

namespace MikanXR
{
	public class BinarySerializer
	{
		private class BinaryWriteVisitor : IVisitor
		{
			//NOTE: BinaryWriter always encodes values in little endian 
			private BinaryWriter _writer;

			public BinaryWriteVisitor(BinaryWriter writer)
			{
				_writer = writer;
			}

			public void visitClass(ValueAccessor accessor)
			{
				var objectValue = accessor.getValueObject();
				var objectType = accessor.ValueType;

				// Writing into the current container element
				Utils.visitObject(objectValue, objectType, this);
			}

			public void visitList(ValueAccessor accessor)
			{
				// Get the list to be serialized
				var list = accessor.getValueObject() as IList;
				Type listType = accessor.ValueType;
				Type elementType = listType.GenericTypeArguments[0];

				// Write the size of the array
				Int32 arraySize = list.Count;
				_writer.Write(arraySize);

				// Serialize each element of the array
				foreach (var element in list)
				{
					// Make a fake "field" for an element in the array
					var elementAccessor = new ValueAccessor(element, elementType);

					// Serialize the element into a json object
					Utils.visitValue(elementAccessor, this);
				}
			}

			public void visitDictionary(ValueAccessor accessor)
			{
				// Get the map to be serialized
				var map = accessor.getValueObject() as IDictionary;
				Type mapType = accessor.ValueType;
				Type keyType = mapType.GenericTypeArguments[0];
				Type valueType = mapType.GenericTypeArguments[1];

				// Write the the number of pairs in the map
				Int32 arraySize = map.Count;
				_writer.Write(arraySize);

				// Serialize each key-value pair in the map
				IDictionaryEnumerator mapEnumerator = map.GetEnumerator();
				while (mapEnumerator.MoveNext())
				{
					// Serialize the key into a json object
					var keyObject = mapEnumerator.Key;
					var keyAccessor = new ValueAccessor(keyObject, keyType);
					Utils.visitValue(keyAccessor, this);

					// Serialize the value into a json object
					var valueObject = mapEnumerator.Value;
					var valueAccessor = new ValueAccessor(valueObject, valueType);
					Utils.visitValue(valueAccessor, this);
				}
			}

			public void visitPolymorphicObject(ValueAccessor accessor)
			{
				// Get the SerializableObject container
				var serializableObject = accessor.getValueObject();
				var serializableObjectType = accessor.ValueType;

				// Get the instance and runtime class name properties
				var instanceClassNameProperty = serializableObjectType.GetProperty("RuntimeClassName");
				var instanceProperty = serializableObjectType.GetProperty("Instance");

				var instanceClassName = (string)instanceClassNameProperty.GetValue(serializableObject);
				var instance = instanceProperty.GetValue(serializableObject);

				Type instanceType = null;
				if (!string.IsNullOrEmpty(instanceClassName) && instance != null)
				{
					instanceType = instance.GetType();
				}

				_writer.WriteUTF8String(instanceClassName);

				bool isValidObject = instance != null;
				_writer.Write(isValidObject);

				// Only bother serializing the object if it's valid
				if (isValidObject)
				{
					Utils.visitObject(instance, instanceType, this);
				}
			}

			public void visitEnum(ValueAccessor accessor)
			{
				object enumObjectValue = accessor.getValueObject();
				string enumStringValue = Enum.GetName(accessor.ValueType, enumObjectValue);

				_writer.WriteUTF8String(enumStringValue);
			}

			public void visitBool(ValueAccessor accessor)
			{
				_writer.Write(accessor.getValue<bool>());
			}

			public void visitByte(ValueAccessor accessor)
			{
				_writer.Write(accessor.getValue<sbyte>());
			}

			public void visitUByte(ValueAccessor accessor)
			{
				_writer.Write(accessor.getValue<byte>());
			}

			public void visitShort(ValueAccessor accessor)
			{
				_writer.Write(accessor.getValue<short>());
			}

			public void visitUShort(ValueAccessor accessor)
			{
				_writer.Write(accessor.getValue<ushort>());
			}

			public void visitInt(ValueAccessor accessor)
			{
				_writer.Write(accessor.getValue<int>());
			}

			public void visitUInt(ValueAccessor accessor)
			{
				_writer.Write(accessor.getValue<uint>());
			}

			public void visitLong(ValueAccessor accessor)
			{
				_writer.Write(accessor.getValue<long>());
			}

			public void visitULong(ValueAccessor accessor)
			{
				_writer.Write(accessor.getValue<ulong>());
			}

			public void visitFloat(ValueAccessor accessor)
			{
				_writer.Write(accessor.getValue<float>());
			}

			public void visitDouble(ValueAccessor accessor)
			{
				_writer.Write(accessor.getValue<double>());
			}

			public void visitString(ValueAccessor accessor)
			{
				_writer.WriteUTF8String(accessor.getValue<string>());
			}

		}

		public static byte[] SerializeToBytes<T>(T instance) where T : class
		{
			return SerializeToBytes(instance, typeof(T));
		}

		public static byte[] SerializeToBytes(object instance, Type structType)
		{
			var writer = new BinaryWriter();
			var visitor = new BinaryWriteVisitor(writer);

			Utils.visitObject(instance, structType, visitor);

			return writer.ToArray();
		}
	}
}
