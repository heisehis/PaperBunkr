<?xml version="1.0"?>
<ReadingList xmlns:xsd="http://www.w3.org/2001/XMLSchema" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
  <Name>4.3 Add Count - Missing published date</Name>
  <Books />
  <Matchers>
    <ComicBookMatcher xsi:type="ComicBookCountMatcher">
      <MatchValue />
    </ComicBookMatcher>
    <ComicBookMatcher xsi:type="ComicBookPublishedMatcher">
      <MatchValue />
    </ComicBookMatcher>
    <ComicBookMatcher xsi:type="ComicBookCustomValuesMatcher" Not="true">
      <MatchValue>comicvine_issue</MatchValue>
    </ComicBookMatcher>
  </Matchers>
</ReadingList>