using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using System.Text.RegularExpressions;
using DomainDrivenDesignPlayground.Model;
using Microsoft.EntityFrameworkCore;

namespace DomainDrivenDesignPlayground.Model
{
	public static class MemoryBinarySerializationPersistenceExtensions
	{
		public static void Save<T>(this T obj, MemoryStream memoryStream) // T muze byt entita (i agregat) nebo value objekt
		{
			if (obj == null) throw new ArgumentNullException(nameof(obj));
			if (memoryStream == null) throw new ArgumentNullException(nameof(memoryStream));

			var binaryFormatter = new BinaryFormatter();
			binaryFormatter.Serialize(memoryStream, obj);
		}

		public static void Save<T>(this T obj, DbContext dbContext) where T : class // T muze byt entita (i agregat) nebo value objekt
		{
			if (obj == null) throw new ArgumentNullException(nameof(obj));
			if (dbContext == null) throw new ArgumentNullException(nameof(dbContext));

			dbContext.Set<T>().Add(obj);
			dbContext.SaveChanges();
		}
	}

	public interface IDomainModelObjectMapper
	{
		TRel Map<TDomain, TRel>(TDomain domainObject);
	}

	public class ReflectionBasedDomainModelObjectMapper : IDomainModelObjectMapper
	{
		public TRel Map<TDomain, TRel>(TDomain domainObject)
		{
			if (domainObject == null) throw new ArgumentNullException(nameof(domainObject));

			var mapped = Activator.CreateInstance<TRel>();
			foreach (var prop in typeof(TRel).GetProperties(BindingFlags.Public | BindingFlags.Instance))
			{
				var domainModelProp = domainObject.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
					.SingleOrDefault(p => p.Name == prop.Name);

				var sourceValue = domainModelProp.GetValue(domainObject);
				prop.SetValue(mapped, sourceValue);
			}

			return mapped;
		}
	}

	public class VisitorBasedDomainModelObjectMapper : IDomainModelObjectMapper
	{
		public TRel Map<TDomain, TRel>(TDomain domainObject)
		{
			if (domainObject == null) throw new ArgumentNullException(nameof(domainObject));
			if(!(domainObject is IVisitable)) throw new ArgumentException($"Cannot map domain object because it is not visitable");

			var visitableDomainObject = domainObject as IVisitable;
			var snapshotingVisitor = new InvoiceSnapshotingVisitor();

			snapshotingVisitor.Visit(visitableDomainObject);

			return snapshotingVisitor.GetSnapshot<TRel>();
		}
	}

	public interface IVisitable
	{
		void Accept(IVisitor visitor);
	}

	public interface IVisitor
	{
		void Visit(IVisitable visitable);
	}

	public class InvoiceSnapshotingVisitor : IVisitor
	{
		private object _currentSnapshot;

		public TSnapshot GetSnapshot<TSnapshot>() => (TSnapshot) _currentSnapshot;

		public void Visit(IVisitable visitable)
		{
			VisitInternal((dynamic) visitable);
			visitable.Accept(this);
		}

		private void VisitInternal(Invoice invoice)
		{
			_currentSnapshot = new InvoiceDto()
			{
				Id = invoice.Id,
				CustomerId = invoice.CustomerId,
			};
		}

		private void VisitInternal(InvoiceItem invoiceItem)
		{
			var invoiceItemSnapshot = new InvoiceItemDto
			{
				ProductCode = invoiceItem.ProductCode.ProdCode,
				Quantity =  invoiceItem.ProductQuantity.Quantity,
			};

			var invoiceSnapshot = _currentSnapshot as InvoiceDto;
			if (invoiceSnapshot == null) return;

			invoiceSnapshot.Items.Add(invoiceItemSnapshot);
		}
	}

	public class InMemoryInvoiceRepository : IInvoiceRepository
	{
		public Invoice FindByCustomerId(Guid customerId)
		{
			// TODO tady loadnu invoice z pameti
			throw new NotImplementedException();
		}

		public void Save(Invoice invoice)
		{
			if (invoice == null) throw new ArgumentNullException(nameof(invoice));

			byte[] buffer = new byte[1024];
			using (var memoryStream = new MemoryStream(buffer, true))
			{
				invoice.Save(memoryStream);
			}
		}
	}

	public interface IInvoiceRepository
	{
		void Save(Invoice invoice);
	}

	public class DbContextInvoiceRepository : IInvoiceRepository
	{
		private readonly IDomainModelObjectMapper _mapper;

		public DbContextInvoiceRepository(IDomainModelObjectMapper mapper)
		{
			_mapper = mapper;
		}

		public Invoice FindByCustomerId(Guid customerId)
		{
			// TODO tady loadnu invoice z databaze pomoci EF Core
			throw new NotImplementedException();
		}

		public void Save(Invoice invoice)
		{
			if (invoice == null) throw new ArgumentNullException(nameof(invoice));

			using (var dbContext = new DbContext(new DbContextOptions<DbContext>()))
			{
				var mapped = _mapper.Map<Invoice, InvoiceDto>(invoice);
				mapped.Save(dbContext);
			}
		}
	}

	public class InvoiceDto
	{
		public Guid Id { get; set; }
		public Guid CustomerId { get; set; }
		public IList<InvoiceItemDto> Items { get; } = new List<InvoiceItemDto>();
	}

	public class InvoiceItemDto
	{
		public int InvoiceItemId { get; set; }  // nemusi byt globalne unikatni, staci kombinace (InvoiceDto.Id, InvoiceItemDto.InvoiceItemId)
		public string ProductCode { get; set; }
		public int Quantity { get; set; }
	}

	public class Invoice : IVisitable // aggregate
	{
		public Guid Id { get; } // id entity
		public Guid CustomerId { get; }
		private readonly IDictionary<ProductCode, InvoiceItem> _items = new Dictionary<ProductCode, InvoiceItem>();

		public Invoice(Guid id, Guid customerId)	// TODO kontrola na empty guids
		{
			Id = id;
			CustomerId = customerId;
		}

		public void AddProductItem(ProductCode prodCode, ProductQuantity quantity)
		{
			if (prodCode == null) throw new ArgumentNullException(nameof(prodCode));
			if (quantity == null) throw new ArgumentNullException(nameof(quantity));

			// kontrola invariantu agregatu
			var item = TryGetInvoiceItem(prodCode);
			if(item != null) throw new InvalidOperationException($"Invoice item already exists for product code {prodCode}");

			_items[prodCode] = new InvoiceItem(prodCode, quantity);
		}

		public InvoiceItem GetInvoiceItem(ProductCode prodCode) // vraci imutabilni objekt, takze invarianty agregatu neni jak zmenit
		{
			if (prodCode == null) throw new ArgumentNullException(nameof(prodCode));

			var item = TryGetInvoiceItem(prodCode);
			if(item == null) throw new InvalidOperationException($"No invoice item found for product code {prodCode}");

			return item;
		}

		public ProductQuantity GetProductQuantity(ProductCode prodCode) // vraci imutabilni objekt, takze invarianty agregatu neni jak zmenit
		{
			if (prodCode == null) throw new ArgumentNullException(nameof(prodCode));

			var item = TryGetInvoiceItem(prodCode);
			if (item == null) throw new InvalidOperationException($"No invoice item found for product code {prodCode}");

			return item.ProductQuantity;
		}

		private InvoiceItem TryGetInvoiceItem(ProductCode prodCode)
		{
			_items.TryGetValue(prodCode, out var item);
			return item;
		}

		public void Accept(IVisitor visitor)
		{
			foreach (var item in _items.Values)
				visitor.Visit(item);
		}
	}

	// imutabilni value objekt
	public class InvoiceItem : IVisitable
	{
		public InvoiceItem(ProductCode productCode, ProductQuantity productQuantity)
		{
			ProductCode = productCode ?? throw new ArgumentNullException(nameof(productCode));
			ProductQuantity = productQuantity ?? throw new ArgumentNullException(nameof(productQuantity));
		}

		public ProductCode ProductCode { get; }
		public ProductQuantity ProductQuantity { get; }

		public void Accept(IVisitor visitor)
		{
		}
	}

	// imutabilni value objekt
	// tohle by byla normalne struktura, ale nevim, jak docilit toho, aby se nedal zavolat bezparametricky
	// konstruktor a obejit tim validacni pravidla; asi bych mohl testovat v konstruktoru InvoiceItem if(productCode == default(ProductCode))...
	public class ProductQuantity
	{
		public ProductQuantity(int quantity)
		{
			if(quantity <= 0 || quantity > 200) throw new ArgumentException($"Parameter {nameof(quantity)} is outside allowed boundaries");

			Quantity = quantity;
		}

		public static ProductQuantity operator +(ProductQuantity pc1, ProductQuantity pc2)
		{
			return new ProductQuantity(pc1.Quantity + pc2.Quantity);
		}

		public int Quantity { get; }
	}

	// imutabilni value objekt
	public class ProductCode
	{
		public string ProdCode { get; }

		public ProductCode(string prodCode)
		{
			if(prodCode == null) throw new ArgumentNullException(nameof(prodCode));
			if(prodCode.Length != 7) throw new ArgumentException($"Parameter {nameof(prodCode)}'s string length should be 7");
			if (!Regex.IsMatch(prodCode, "^[A-Z]{4}[0-9]{3}$"))
				throw new ArgumentException($"Parameter {nameof(prodCode)} does not satisfy the necessary format.");

			ProdCode = prodCode;
		}
	}
}
