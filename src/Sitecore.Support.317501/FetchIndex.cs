using Sitecore.ContentSearch;
using Sitecore.ContentSearch.Abstractions;
using Sitecore.ContentSearch.Pipelines.GetContextIndex;
using Sitecore.Data.Items;
using Sitecore.Diagnostics;
using Sitecore.Reflection;
using Sitecore.SecurityModel;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Sitecore.Support.ContentSearch.Pipelines.GetContextIndex
{
  public class FetchIndex : GetContextIndexProcessor
  {
    private readonly ISettings settings;

    public FetchIndex()
    {
      settings = ContentSearchManager.Locator.GetInstance<ISettings>();
    }

    internal FetchIndex(ISettings settings)
    {
      this.settings = settings;
    }

    public override void Process(GetContextIndexArgs args)
    {
      if (args != null && args.Result == null)
      {
        args.Result = GetContextIndex(args.Indexable, args);
      }
    }

    protected virtual string GetContextIndex(IIndexable indexable, GetContextIndexArgs args)
    {
      try
      {
        if (indexable == null)
        {
          return null;
        }
        List<ISearchIndex> list = new List<ISearchIndex>();
        foreach (ISearchIndex index in ContentSearchManager.Indexes)
        {
          AbstractSearchIndex abstractSearchIndex = index as AbstractSearchIndex;
          if ((abstractSearchIndex == null || abstractSearchIndex.IsInitialized) && index.Crawlers.Any((IProviderCrawler c) => !c.IsExcludedFromIndex(indexable)))
          {
            list.Add(index);
          }
        }
        IEnumerable<ISearchIndex> enumerable = list.AsEnumerable();
        if (!enumerable.Any())
        {
          enumerable = FindIndexesRelatedToIndexable(args.Indexable, ContentSearchManager.Indexes);
        }
        IEnumerable<Tuple<ISearchIndex, int>> enumerable2 = RankContextIndexes(enumerable, indexable);
        Tuple<ISearchIndex, int>[] array = (enumerable2 as Tuple<ISearchIndex, int>[]) ?? enumerable2.ToArray();
        if (!array.Any())
        {
          Log.Error($"There is no appropriate index for {indexable.AbsolutePath} - {indexable.Id}. You have to add an index crawler that will cover this item", this);
          return null;
        }
        if (array.Count() == 1)
        {
          return array.First().Item1.Name;
        }
        if (array.First().Item2 < array.Skip(1).First().Item2)
        {
          return array.First().Item1.Name;
        }
        string setting = settings.GetSetting("ContentSearch.DefaultIndexType", "");
        Type defaultType = ReflectionUtil.GetTypeInfo(setting);
        if (defaultType == null)
        {
          return array[0].Item1.Name;
        }
        Tuple<ISearchIndex, int>[] array2 = (from i in array
          where i.Item1.GetType() == defaultType
          orderby i.Item1.Name
          select i).ToArray();
        if (!array2.Any())
        {
          return array[0].Item1.Name;
        }
        return array2[0].Item1.Name;
      }
      catch (System.InvalidOperationException)
      {
        Log.Warn(
          "Sitecore.Support.317501: " + args.Indexable.AbsolutePath + " is excluded and cannot be covered by any index.",
          this);
        return null;
      }
    }

    protected virtual IEnumerable<ISearchIndex> FindIndexesRelatedToIndexable(IIndexable indexable, IEnumerable<ISearchIndex> indexes)
    {
      SitecoreIndexableItem sitecoreIndexableItem = indexable as SitecoreIndexableItem;
      if (sitecoreIndexableItem == null)
      {
        return new List<ISearchIndex>();
      }
      Item item = sitecoreIndexableItem.Item;
      using (new SecurityDisabler())
      {
        using (new WriteCachesDisabler())
        {
          return from i in indexes
                 where (from crawler in i.Crawlers.OfType<SitecoreItemCrawler>()
                        where crawler.RootItem != null
                        select crawler).Any(delegate (SitecoreItemCrawler crawler)
                        {
                          if (item.Database.Name.Equals(crawler.Database, StringComparison.InvariantCultureIgnoreCase))
                          {
                            return item.Paths.LongID.StartsWith(crawler.RootItem.Paths.LongID, StringComparison.InvariantCulture);
                          }
                          return false;
                        })
                 select i;
        }
      }
    }

    protected virtual IEnumerable<Tuple<ISearchIndex, int>> RankContextIndexes(IEnumerable<ISearchIndex> indexes, IIndexable indexable)
    {
      return from i in indexes.Distinct()
             select Tuple.Create(i, (i is IContextIndexRankable) ? ((IContextIndexRankable)i).GetContextIndexRanking(indexable) : 2147483647) into i
             orderby i.Item2
             select i;
    }
  }
}