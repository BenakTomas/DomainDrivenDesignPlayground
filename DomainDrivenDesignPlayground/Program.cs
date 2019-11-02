using System;
using DomainDrivenDesignPlayground.Model;

namespace DomainDrivenDesignPlayground
{
	class Program
	{
		static void Main(string[] args)
		{
			var invoice = new Invoice(Guid.NewGuid(), Guid.NewGuid());
			invoice.AddProductItem(new ProductCode("ABIB123"), new ProductQuantity(100));
			invoice.AddProductItem(new ProductCode("MIKE456"), new ProductQuantity(150));

			IInvoiceRepository invoiceRepo = new DbContextInvoiceRepository(new VisitorBasedDomainModelObjectMapper());
			invoiceRepo.Save(invoice);
		}
	}
}
