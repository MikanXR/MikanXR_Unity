using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections;
using System.Reflection;

namespace MikanXR
{
	public class JsonSerializer
	{
		private class JsonWriteVisitor : IVisitor
		{
			private JToken _jsonToken;
			public JToken jsonToken => _jsonToken;

			public JsonWriteVisitor(JToken jsonToken)
			{
				_jsonToken = jsonToken;
			}

			public void visitClass(ValueAccessor accessor)
			{
				var objectValue = accessor.getValueObject();
				var objectType = accessor.ValueType;

				var jsonObject = new JObject();
				var jsonVisitor = new JsonWriteVisitor(jsonObject);
				Utils.visitObject(objectValue, objectType, jsonVisitor);

				setJsonValueFromJsonToken(accessor.ValueField, jsonObject);
			}

			public void visitList(ValueAccessor accessor)
			{
				// Get the list to be serialized
				var list = accessor.getValueObject() as IList;
				Type listType = accessor.ValueType;
				Type elementType = listType.GenericTypeArguments[0];

				// Serialize each element of the array
				JArray jsonArray = new JArray();
				foreach (var element in list)
				{
					// Make a fake "field" for an element in the array
					var elementAccessor = new ValueAccessor(element, elementType);

					// Serialize the element into a json object
					var elementVisitor = new JsonWriteVisitor(null);
					Utils.visitValue(elementAccessor, elementVisitor);

					// Add the element to the json array
					jsonArray.Add(elementVisitor.jsonToken);
				}

				setJsonValueFromJsonToken(accessor.ValueField, jsonArray);
			}

			public void visitDictionary(ValueAccessor accessor)
			{
				// Get the map to be serialized
				var map = accessor.getValueObject() as IDictionary;
				Type mapType = accessor.ValueType;
				Type keyType = mapType.GenericTypeArguments[0];
				Type valueType = mapType.GenericTypeArguments[1];

				// Serialize each key-value pair in the map
				JArray jsonPairArray = new JArray();
				IDictionaryEnumerator mapEnumerator = map.GetEnumerator();
				while (mapEnumerator.MoveNext())
				{
					// Serialize the key into a json object
					var keyObject = mapEnumerator.Key;
					var keyVisitor = new JsonWriteVisitor(null);
					var keyAccessor = new ValueAccessor(keyObject, keyType);
					Utils.visitValue(keyAccessor, keyVisitor);

					// Serialize the value into a json object
					var valueObject = mapEnumerator.Value;
					var valueVisitor = new JsonWriteVisitor(null);
					var valueAccessor = new ValueAccessor(valueObject, valueType);
					Utils.visitValue(valueAccessor, valueVisitor);

					var jsonPair = new JObject();
					jsonPair["key"]= keyVisitor.jsonToken;
					jsonPair["value"]= valueVisitor.jsonToken;

					// Add the pairs to the json array
					jsonPairArray.Add(jsonPair);
				}

				setJsonValueFromJsonToken(accessor.ValueField, jsonPairArray);
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

				var jsonRuntimeObject = new JObject();

				if (!string.IsNullOrEmpty(instanceClassName) && instance != null)
				{
					Type instanceType = instance.GetType();

					// Serialize the object into a json object
					var jsonVisitor = new JsonWriteVisitor(jsonRuntimeObject);
					Utils.visitObject(instance, instanceType, jsonVisitor);
				}

				var jsonSerializableObject = new JObject();
				jsonSerializableObject["class_name"] = instanceClassName;
				jsonSerializableObject["value"] = jsonRuntimeObject;

				setJsonValueFromJsonToken(accessor.ValueField, jsonSerializableObject);
			}

			public void visitEnum(ValueAccessor accessor)
			{
				object enumObjectValue = accessor.getValueObject();
				string enumStringValue = Enum.GetName(accessor.ValueType, enumObjectValue);

				setJsonValueFromJsonToken(accessor.ValueField, enumStringValue);
			}

			public void visitBool(ValueAccessor accessor)
			{
				setJsonValueFromAccessor<bool>(accessor);
			}

			public void visitByte(ValueAccessor accessor)
			{
				setJsonValueFromAccessor<sbyte>(accessor);
			}

			public void visitUByte(ValueAccessor accessor)
			{
				setJsonValueFromAccessor<byte>(accessor);
			}

			public void visitShort(ValueAccessor accessor)
			{
				setJsonValueFromAccessor<short>(accessor);
			}

			public void visitUShort(ValueAccessor accessor)
			{
				setJsonValueFromAccessor<ushort>(accessor);
			}

			public void visitInt(ValueAccessor accessor)
			{
				setJsonValueFromAccessor<int>(accessor);
			}

			public void visitUInt(ValueAccessor accessor)
			{
				setJsonValueFromAccessor<uint>(accessor);
			}

			public void visitLong(ValueAccessor accessor)
			{
				setJsonValueFromAccessor<long>(accessor);
			}

			public void visitULong(ValueAccessor accessor)
			{
				throw new Exception(
					"JsonWriteVisitor::visitULong() " +
					"ULong Accessor " + accessor.ValueName +
					" type not supported by all JSON libraries");
			}

			public void visitFloat(ValueAccessor accessor)
			{
				setJsonValueFromAccessor<float>(accessor);
			}

			public void visitDouble(ValueAccessor accessor)
			{
				setJsonValueFromAccessor<double>(accessor);
			}
			public void visitString(ValueAccessor accessor)
			{
				var sourceField = accessor.ValueField;
				string value = accessor.getValue<string>();

				setJsonValueFromJsonToken(sourceField, new JValue(value ?? string.Empty));
			}

			private void setJsonValueFromAccessor<T>(ValueAccessor accessor)
			{
				var sourceField= accessor.ValueField;
				T value = accessor.getValue<T>();

				setJsonValueFromJsonToken(sourceField, new JValue(value));
			}

			private void setJsonValueFromJsonToken(FieldInfo sourceField, JToken token)
			{
				if (sourceField != null)
				{
					var ownerJsonObject = _jsonToken as JObject;
					ownerJsonObject.Add(sourceField.Name, token);
				}
				else
				{
					_jsonToken = token;
				}
			}
		}

		public static string serializeToJsonString<T>(T instance) where T : class
		{
			return serializeToJsonString(instance, typeof(T));
		}

		public static string serializeToJsonString(object instance, Type structType)
		{
			JObject json = serializeToJson(instance, structType);
			return json.ToString();
		}

		public static JObject serializeToJson<T>(T instance) where T : class
		{
			return serializeToJson(instance, typeof(T));
		}

		public static JObject serializeToJson(object instance, Type structType)
		{
			var jsonObject = new JObject();
			var visitor = new JsonWriteVisitor(jsonObject);

			Utils.visitObject(instance, structType, visitor);

			return jsonObject;
		}
	}
}
