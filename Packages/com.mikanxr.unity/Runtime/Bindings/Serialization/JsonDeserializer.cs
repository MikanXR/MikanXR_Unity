using Newtonsoft.Json.Linq;
using System;
using System.Collections;
using System.Linq;
using System.Reflection;

namespace MikanXR
{
	public class JsonDeserializer
	{
		private class JsonReadVisitor : IVisitor
		{
			private Assembly _contextAssembly;
			private JToken _jsonToken;
			public JToken jsonToken => _jsonToken;

			public JsonReadVisitor(JToken jsonToken, Assembly contextAssembly)
			{
				_jsonToken = jsonToken;
				_contextAssembly = contextAssembly;
			}

			public void visitClass(ValueAccessor accessor)
			{
				JToken jsonToken = getJsonTokenFromAccessor(accessor);
				Type fieldType = accessor.ValueType;
				string fieldName = accessor.ValueName;

				if (jsonToken.Type == JTokenType.Object)
				{
					accessor.ensureValueAllocated();
					object fieldInstance = accessor.getValueObject();

					JsonReadVisitor jsonVisitor = new JsonReadVisitor(jsonToken as JObject, _contextAssembly);
					Utils.visitObject(fieldInstance, fieldType, jsonVisitor);
				}
				else
				{
					throw new Exception(
						"JsonReadVisitor::visitClass() Field " + fieldName +
						" missing corresponding json object");
				}
			}

			public void visitList(ValueAccessor accessor)
			{
				JToken jsonToken = getJsonTokenFromAccessor(accessor);
				Type fieldType = accessor.ValueType;
				string fieldName = accessor.ValueName;

				if (jsonToken.Type == JTokenType.Array)
				{
					var jsonArray = jsonToken as JArray;

					accessor.ensureValueAllocated();
					var list= accessor.getValueObject() as IList;
					Type elementType = fieldType.GenericTypeArguments[0];

					list.Clear();
					foreach (var jsonElement in jsonArray)
					{
						// Make a fake "field" for an element in the array
						var elementAccessor = new ValueAccessor(null, elementType);

						// Deserialize the element
						JsonReadVisitor elementVisitor = new JsonReadVisitor(jsonElement, _contextAssembly);
						Utils.visitValue(elementAccessor, elementVisitor);

						// Add the element to the list
						list.Add(elementAccessor.ValueInstance);
					}
				}
				else
				{
					throw new Exception(
						"JsonReadVisitor::visitList() Field " + fieldName +
						" missing corresponding json array");
				}
			}

			public void visitDictionary(ValueAccessor accessor)
			{
				JToken jsonToken = getJsonTokenFromAccessor(accessor);
				Type fieldType = accessor.ValueType;
				string fieldName = accessor.ValueName;

				if (jsonToken.Type == JTokenType.Array)
				{
					var jsonMapPairArray = jsonToken as JArray;

					accessor.ensureValueAllocated();
					var map = accessor.getValueObject() as IDictionary;
					Type keyType = fieldType.GenericTypeArguments[0];
					Type valueType = fieldType.GenericTypeArguments[1];

					map.Clear();
					foreach (var jsonPair in jsonMapPairArray)
					{
						// Make a fake "field" for the key
						var keyAccessor = new ValueAccessor(null, keyType);

						// Deserialize the key
						var jsonKey= jsonPair["key"];
						JsonReadVisitor keyVisitor = new JsonReadVisitor(jsonKey, _contextAssembly);
						Utils.visitValue(keyAccessor, keyVisitor);

						// Make a fake "field" for the value
						var valueAccessor = new ValueAccessor(null, valueType);

						// Deserialize the value
						var jsonValue = jsonPair["value"];
						JsonReadVisitor valueVisitor = new JsonReadVisitor(jsonValue, _contextAssembly);
						Utils.visitValue(valueAccessor, valueVisitor);

						// Add the key-value pair to the map
						map.Add(keyAccessor.ValueInstance, valueAccessor.ValueInstance);
					}
				}
				else
				{
					throw new Exception(
						"JsonReadVisitor::visitList() Field " + fieldName +
						" missing corresponding json array");
				}
			}

			public void visitPolymorphicObject(ValueAccessor accessor)
			{
				JToken jsonToken = getJsonTokenFromAccessor(accessor);
				Type fieldType = accessor.ValueType;
				string fieldName = accessor.ValueName;

				accessor.ensureValueAllocated();
				object fieldInstance = accessor.getValueObject();

				// Allocate the element instance using the runtime class name
				var elementRuntimeTypeName = (string)jsonToken["class_name"];
				if (elementRuntimeTypeName == "")
					return;

                // Find the runtime type in the context assembly
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
							"JsonReadVisitor::visitPolymorphicObject() " +
							"TypedObjectPtr Accessor " + fieldName +
							" used an unknown runtime class_name: " + elementRuntimeTypeName);
				}
				var elementInstance = Activator.CreateInstance(elementRuntimeType);

				// Deserialize the element into the newly allocated instance
				JObject elementJson = jsonToken["value"] as JObject;
				var elementVisitor = new JsonReadVisitor(elementJson, _contextAssembly);
				Utils.visitObject(elementInstance, elementRuntimeType, elementVisitor);

				// Assign the child object to the SerializableObject field
				MethodInfo setInstanceMethod = fieldType.GetMethod("setInstance");
				setInstanceMethod.Invoke(fieldInstance, new object[] { elementInstance });
			}

			public void visitEnum(ValueAccessor accessor)
			{
				JToken jsonToken = getJsonTokenFromAccessor(accessor);

				if (jsonToken.Type == JTokenType.Integer)
				{
					int value = (int)jsonToken;
					var enumValue= Enum.ToObject(accessor.ValueType, value);

					accessor.setValueObject(enumValue);
				}
				else if (jsonToken.Type == JTokenType.String)
				{
					string value = (string)jsonToken;
					var enumValue= Enum.Parse(accessor.ValueType, value);

					accessor.setValueObject(enumValue);
				}
				else
				{
					throw new Exception(
						"JsonReadVisitor::visitEnum() " +
						"Enum Accessor " + accessor.ValueName +
						" was not a Integer or String json value");
				}
			}

			public void visitBool(ValueAccessor accessor)
			{
				JToken jsonToken = getJsonTokenFromAccessor(accessor);

				if (jsonToken.Type == JTokenType.Boolean)
				{
					bool value = (bool)jsonToken;

					accessor.setValue(value);
				}
				else
				{
					throw new Exception(
						"JsonReadVisitor::visitBool() " +
						"Bool Accessor " + accessor.ValueName +
						" was not a bool json value");
				}
			}

			public void visitByte(ValueAccessor accessor)
			{
				JToken jsonToken = getJsonTokenFromAccessor(accessor);

				if (jsonToken.Type == JTokenType.Integer)
				{
					sbyte value = (sbyte)(short)jsonToken;

					accessor.setValue(value);
				}
				else
				{
					throw new Exception(
						"JsonReadVisitor::visitByte() " +
						"Byte Accessor " + accessor.ValueName +
						" was not a Integer json value");
				}
			}

			public void visitUByte(ValueAccessor accessor)
			{
				JToken jsonToken = getJsonTokenFromAccessor(accessor);

				if (jsonToken.Type == JTokenType.Integer)
				{
					byte value = (byte)(ushort)jsonToken;

					accessor.setValue(value);
				}
				else
				{
					throw new Exception(
						"JsonReadVisitor::visitUByte() " +
						"UByte Accessor " + accessor.ValueName +
						" was not a Integer json value");
				}
			}

			public void visitShort(ValueAccessor accessor)
			{
				JToken jsonToken = getJsonTokenFromAccessor(accessor);

				if (jsonToken.Type == JTokenType.Integer)
				{
					short value = (short)jsonToken;

					accessor.setValue(value);
				}
				else
				{
					throw new Exception(
						"JsonReadVisitor::visitShort() " +
						"Short Accessor " + accessor.ValueName +
						" was not a Integer json value");
				}
			}

			public void visitUShort(ValueAccessor accessor)
			{
				JToken jsonToken = getJsonTokenFromAccessor(accessor);

				if (jsonToken.Type == JTokenType.Integer)
				{
					ushort value = (ushort)jsonToken;

					accessor.setValue(value);
				}
				else
				{
					throw new Exception(
						"JsonReadVisitor::visitUShort() " +
						"UShort Accessor " + accessor.ValueName +
						" was not a Integer json value");
				}
			}

			public void visitInt(ValueAccessor accessor)
			{
				JToken jsonToken = getJsonTokenFromAccessor(accessor);

				if (jsonToken.Type == JTokenType.Integer)
				{
					int value = (int)jsonToken;

					accessor.setValue(value);
				}
				else
				{
					throw new Exception(
						"JsonReadVisitor::visitInt() " +
						"Int Accessor " + accessor.ValueName +
						" was not a Integer json value");
				}
			}

			public void visitUInt(ValueAccessor accessor)
			{
				JToken jsonToken = getJsonTokenFromAccessor(accessor);

				if (jsonToken.Type == JTokenType.Integer)
				{
					uint value = (uint)jsonToken;

					accessor.setValue(value);
				}
				else
				{
					throw new Exception(
						"JsonReadVisitor::visitUInt() " +
						"UInt Accessor " + accessor.ValueName +
						" was not a Integer json value");
				}
			}

			public void visitLong(ValueAccessor accessor)
			{
				JToken jsonToken = getJsonTokenFromAccessor(accessor);

				if (jsonToken.Type == JTokenType.Integer)
				{
					long value = (long)jsonToken;

					accessor.setValue(value);
				}
				else
				{
					throw new Exception(
						"JsonReadVisitor::visitLong() " +
						"Long Accessor " + accessor.ValueName +
						" was not a Integer json value");
				}
			}

			public void visitULong(ValueAccessor accessor)
			{
				throw new Exception(
					"JsonReadVisitor::visitULong() " +
					"ULong Accessor " + accessor.ValueName +
					" type not supported by all JSON libraries");
			}

			public void visitFloat(ValueAccessor accessor)
			{
				JToken jsonToken = getJsonTokenFromAccessor(accessor);

				if (jsonToken.Type == JTokenType.Integer ||
					jsonToken.Type == JTokenType.Float)
				{
					float value = (float)jsonToken;

					accessor.setValue(value);
				}
				else
				{
					throw new Exception(
						"JsonReadVisitor::visitFloat() " +
						"Float Accessor " + accessor.ValueName +
						" was not a Float json value");
				}
			}

			public void visitDouble(ValueAccessor accessor)
			{
				JToken jsonToken = getJsonTokenFromAccessor(accessor);

				if (jsonToken.Type == JTokenType.Integer ||
					jsonToken.Type == JTokenType.Float)
				{
					double value = (double)jsonToken;

					accessor.setValue(value);
				}
				else
				{
					throw new Exception(
						"JsonReadVisitor::visitDouble() " +
						"Double Accessor " + accessor.ValueName +
						" was not a Double json value");
				}
			}

			public void visitString(ValueAccessor accessor)
			{
				JToken jsonToken = getJsonTokenFromAccessor(accessor);

				if (jsonToken.Type == JTokenType.String)
				{
					string value = (string)jsonToken;

					accessor.setValue(value);
				}
				else
				{
					throw new Exception(
						"JsonReadVisitor::visitString() " +
						"String Accessor " + accessor.ValueName +
						" was not a String json value");
				}
			}

			private JToken getJsonTokenFromAccessor(ValueAccessor accessor)
			{
				var field= accessor.ValueField;
				if (field != null)
				{
					var fieldName = field.Name;
					var jsonObject = _jsonToken as JObject;

					if (jsonObject.TryGetValue(fieldName, out var childJsonObject))
					{
						return childJsonObject;
					}

					throw new Exception("Field "+fieldName+" not found in json");
				}
				else
				{
					// The json object should contain the value we want to deserialize
					return _jsonToken;
				}
			}
		}

		public static bool deserializeFromJsonString<T>(string jsonString, T instance) where T : class
		{
			return deserializeFromJsonString(jsonString, instance, typeof(T));
		}

		public static bool deserializeFromJsonString(
			string jsonString, 
			object instance, 
			Type structType)
		{		
			try
			{
				JObject jsonObject = JObject.Parse(jsonString);
				return deserializeFromJson(jsonObject, instance, structType);
			}
			catch (Exception e)
			{
				Console.WriteLine("Failed to deserialize from JSON string: " + e.Message);
				return false;
			}
		}

		public static bool deserializeFromJson<T>(JObject jsonObject, T instance) where T : class
		{
			return deserializeFromJson(jsonObject, instance, typeof(T));
		}

		public static bool deserializeFromJson(JObject jsonObject, object instance, Type structType)
		{
			try
			{
				var visitor = new JsonReadVisitor(jsonObject, structType.Assembly);

				Utils.visitObject(instance, structType, visitor);

				return true;
			}
			catch (Exception e)
			{
				Console.WriteLine("Failed to deserialize from JSON: " + e.Message);
				return false;
			}
		}
	}
}
