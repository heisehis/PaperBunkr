<?xml version="1.0"?>
<ReadingList xmlns:xsd="http://www.w3.org/2001/XMLSchema" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
  <Name>8.6 Alternate Universes now has a Main</Name>
  <Books />
  <Matchers>
    <ComicBookMatcher xsi:type="ComicBookCustomValuesMatcher">
      <MatchValue>DataManager_processed</MatchValue>
      <MatchValue2>yes</MatchValue2>
    </ComicBookMatcher>
    <ComicBookMatcher xsi:type="ComicBookCustomValuesMatcher" Not="true">
      <MatchValue>comicvine_issue</MatchValue>
    </ComicBookMatcher>
    <ComicBookMatcher xsi:type="ComicBookFormatMatcher" Not="true">
      <MatchValue />
    </ComicBookMatcher>
    <ComicBookMatcher xsi:type="ComicBookFullPathMatcher" MatchOperator="2">
      <MatchValue>Non Universe|Alternate Universes</MatchValue>
    </ComicBookMatcher>
    <ComicBookMatcher xsi:type="ComicBookMainCharacterOrTeamMatcher" Not="true">
      <MatchValue />
    </ComicBookMatcher>
  </Matchers>
</ReadingList>