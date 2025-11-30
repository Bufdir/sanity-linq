using System.Collections.Generic;
using Sanity.Linq.CommonTypes;

namespace Sanity.Linq.Demo.Model;

public class Table : SanityDocument
{
    public string Title { get; set; } = string.Empty;

    public bool Bootstrap { get; set; }

    //TODO: add bootstrap options

    public List<TableRow> Rows { get; set; } = [];

}

public class TableRow : SanityObject
{
    public string[] Cells { get; set; } = [];
}