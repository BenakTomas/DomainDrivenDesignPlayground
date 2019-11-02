using System.IO;
using System.Runtime.Serialization.Formatters.Binary;

namespace DomainDrivenDesignPlayground.Model.Extension
{
	public static class DbContextPersistenceExtensions
	{
		public static void Save<T>(this T obj) // T muze byt entita (i agregat) nebo value objekt
		{
			byte[] buffer = new byte[1024];
			using (var memoryStream = new MemoryStream(buffer, true))
			{
				var binaryFormatter = new BinaryFormatter();
				binaryFormatter.Serialize(memoryStream, obj);
			}
		}
	}
}