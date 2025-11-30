using Newtonsoft.Json.Linq;

namespace Sanity.Linq.BlockContent;

public class SanityTreeBuilder
{
    public JArray Build(JArray blockArray)
    {
        // set list trees / listItem = bullet | number && level != null
        var currentListType = "";
        for (var i = 0; i < blockArray.Count; i++)
        {
            var item = blockArray[i] as JObject;
            if (item == null)
            {
                continue;
            }

            if ((string?)blockArray[i]["listItem"] == "bullet")
            {
                //check if first in bullet array
                if (currentListType == "" && !item.ContainsKey("firstItem"))
                {
                    item.Add(new JProperty("firstItem", true));
                }

                currentListType = "bullet";

                // check if last in array, also last in bullet array 
                if (blockArray.Count == i + 1)
                {
                    if (!item.ContainsKey("lastItem"))
                    {
                        item.Add(new JProperty("lastItem", true));
                    }
                    currentListType = "";
                    break;
                }

                //in the middle of array but last of bullet array
                var nextListItem = (string?)blockArray[i + 1]["listItem"];
                if (currentListType == "bullet" && (nextListItem == null || nextListItem == "number"))
                {
                    if (!item.ContainsKey("lastItem"))
                    {
                        item.Add(new JProperty("lastItem", true));
                    }
                    currentListType = "";
                }
            }

            if ((string?)blockArray[i]["listItem"] != "number")
            {
                continue;
            }

            //check if first in bullet array
            if (currentListType == "" && !item.ContainsKey("firstItem"))
            {
                item.Add(new JProperty("firstItem", true));
            }

            currentListType = "number";

            // check if last in array, also last in bullet array 
            if (blockArray.Count == i + 1)
            {
                if (!item.ContainsKey("lastItem"))
                {
                    item.Add(new JProperty("lastItem", true));
                }
                currentListType = "";
                break;
            }

            //in the middle of array but last of bullet array
            var nextListItem2 = (string?)blockArray[i + 1]["listItem"];
            if (currentListType == "number" && (nextListItem2 == null || nextListItem2 == "bullet"))
            {
                if (!item.ContainsKey("lastItem"))
                {
                    item.Add(new JProperty("lastItem", true));
                }
                currentListType = "";
            }
        }

        return blockArray;
    }
}