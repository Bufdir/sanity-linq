using System.Threading.Tasks;
using Sanity.Linq.Demo.Model;
using Xunit;

namespace Sanity.Linq.Tests;

public class CustomSerializersTest : TestBase
{
    [Fact]
    public Task SerializeToBootstrapTable()
    {
        // Test of DataTable Object https://github.com/fredjens/sanity-datatable

        var post = new Table
        {
            Title = "Test Table",
            Bootstrap = false,
            Rows =
            [
                new TableRow
                {
                    Cells = ["", "", ""] //first row is headers
                }
            ]
        };
        return Task.CompletedTask;

        //get document from sanity
        //build
        //set marks
        //return html
    }
}