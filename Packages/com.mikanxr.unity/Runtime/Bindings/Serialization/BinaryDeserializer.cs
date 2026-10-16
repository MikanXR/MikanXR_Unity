using System;
using System.Collections;
using System.Linq;
using System.Reflection;

namespace MikanXR
{
	public class BinaryDeserializer
	{
		private class BinaryReadVisitor : IVisitor
		{
			private Assembly _contextAssembly;
			private BinaryReader _reader;

			public BinaryReadVisitor(BinaryReader reader, Assembly contextAssembly)
			{
				_reader = reader;
				_contextAssembly = contextAssembly;
			}

			public void visitClass(ValueAccessor accessor)
			{
				accessor.ensureValueAllocated();
				var objectValue = accessor.getValueObject();
				var objectType = accessor.ValueType;

				// Writing into the current container element
				Utils.visitObject(objectValue, objectType, this);
			}

			public void visitList(ValueAccessor accessor)
			{
				// Get the list to be deserialized
				accessor.ensureValueAllocated();
				var list = accessor.getValueObject() as IList;
				Type listType = accessor.ValueType;
				Type elementType = listType.GenericTypeArguments[0];

				// Read the size of the array
				Int32 arraySize = _reader.ReadInt32();

				// Serialize each element of the array
				list.Clear();
				for (Int32 arrayIndex = 0; arrayIndex < arraySize; arrayIndex++)
				{
					// Make a fake "field" for an element in the array
					var elementAccessor = new ValueAccessor(null, elementType);

					// Serialize the element into a json object
					Utils.visitValue(elementAccessor, this);

					// Add the element to the list
					list.Add(elementAccessor.ValueInstance);
				}
			}

			public void visitDictionary(ValueAccessor accessor)
			{
				// Get the map to be deserialized
				accessor.ensureValueAllocated();
				var map = accessor.getValueObject() as IDictionary;
				Type mapType = accessor.ValueType;
				Type keyType = mapType.GenericTypeArguments[0];
				Type valueType = mapType.GenericTypeArguments[1];

				// Read the size of the array
				Int32 arraySize = _reader.ReadInt32();

				// Serialize each element of the array
				map.Clear();
				for (Int32 pairIndex = 0; pairIndex < arraySize; pairIndex++)
				{
					// Deserialize the key
					var keyAccessor = new ValueAccessor(null, keyType);
					Utils.visitValue(keyAccessor, this);

					// Deserialize the value
					var valueAccessor = new ValueAccessor(null, valueType);
					Utils.visitValue(valueAccessor, this);

					// Add the element to the list
					map.Add(keyAccessor.ValueInstance, valueAccessor.ValueInstance);
				}
			}

			public void visitPolymorphicObject(ValueAccessor accessor)
			{
				// Get the SerializableObject container
				accessor.ensureValueAllocated();
				var serializableObject = accessor.getValueObject();
				var serializableObjectType = accessor.ValueType;
				string fieldName = accessor.ValueName;

				// Read the runtime class info
				string elementRuntimeTypeName = _reader.ReadUTF8String();

				bool isValidObject = _reader.ReadBoolean();
				if (isValidObject)
				{
					// Allocate instance by class name
					var elementRuntimeTypes =
							from t in _contextAssembly.GetTypes()
							where
								t.IsClass &&
								t.Name == elementRuntimeTypeName &&
								t.IsSubclassOf(typeof(PolymorphicStruct))
							select t;
					var elementRuntimeType = elementRuntimeTypes.FirstOrDefault();
					if (elementRuntimeType == null)
					{
						throw new Exception(
								"BinaryDeserializer::visitPolymorphicObject() " +
								"TypedObjectPtr Accessor " + fieldName +
								" used an unknown runtime class_name: " + elementRuntimeTypeName);
					}
					var elementInstance = Activator.CreateInstance(elementRuntimeType);

					// Deserialize the instance
					Utils.visitObject(elementInstance, elementRuntimeType, this);

					// Assign the child object to the PolymorphicObject field
					MethodInfo setInstanceMethod = serializableObjectType.GetMethod("setInstance");
					setInstanceMethod.Invoke(serializableObject, new object[] { elementInstance });
				}
			}

			public void visitEnum(ValueAccessor accessor)
			{
				string enumStringValue = _reader.ReadUTF8String();
				var enumValue= Enum.Parse(accessor.ValueType, enumStringValue);

				accessor.setValueObject(enumValue);
			}

			public void visitBool(ValueAccessor accessor)
			{
				bool value = _reader.ReadBoolean();

				accessor.setValue(value);
			}

			public void visitByte(ValueAccessor accessor)
			{
				sbyte value = _reader.ReadSByte();

				accessor.setValue(value);
			}

			public void visitUByte(ValueAccessor accessor)
			{
				byte value = _reader.ReadByte();

				accessor.setValue(value);
			}

			public void visitShort(ValueAccessor accessor)
			{
				Int16 value = _reader.ReadInt16();

				accessor.setValue(value);
			}

			public void visitUShort(ValueAccessor accessor)
			{
				UInt16 value = _reader.ReadUInt16();

				accessor.setValue(value);
			}

			public void visitInt(ValueAccessor accessor)
			{
				Int32 value = _reader.ReadInt32();

				accessor.setValue(value);
			}

			public void visitUInt(ValueAccessor accessor)
			{
				UInt32 value = _reader.ReadUInt32();

				accessor.setValue(value);
			}

			public void visitLong(ValueAccessor accessor)
			{
				Int64 value = _reader.ReadInt64();

				accessor.setValue(value);
			}

			public void visitULong(ValueAccessor accessor)
			{
				UInt64 value = _reader.ReadUInt64();

				accessor.setValue(value);
			}

			public void visitFloat(ValueAccessor accessor)
			{
				float value = _reader.ReadSingle();

				accessor.setValue(value);
			}

			public void visitDouble(ValueAccessor accessor)
			{
				double value = _reader.ReadDouble();

				accessor.setValue(value);
			}

			public void visitString(ValueAccessor accessor)
			{
				string value = _reader.ReadUTF8String();

				accessor.setValue(value);
			}
		}

		public static bool DeserializeFromBytes<T>(byte[] inBytes, T instance) where T : class
		{
			return DeserializeFromBytes(inBytes, instance, typeof(T));
		}

		public static bool DeserializeFromBytes(
			byte[] inBytes,
			object instance,
			Type structType)
		{
			try
			{
				var binaryReader = new BinaryReader(inBytes);
				var visitor = new BinaryReadVisitor(binaryReader, structType.Assembly);
				Utils.visitObject(instance, structType, visitor);

				return true;
			}
			catch (Exception e)
			{
				Console.WriteLine("Failed to deserialize from JSON string: " + e.Message);
				return false;
			}
		}
	}
}
