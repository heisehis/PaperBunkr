<?xml version="1.0"?>
<ReadingList xmlns:xsd="http://www.w3.org/2001/XMLSchema" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
  <Name>2.3 ComicVine Scrapper TPB</Name>
  <Books />
  <Matchers>
    <ComicBookMatcher xsi:type="ComicBookGroupMatcher">
      <Matchers>
        <ComicBookMatcher xsi:type="ComicBookCustomValuesMatcher">
          <MatchValue>comicvine_issue</MatchValue>
        </ComicBookMatcher>
      </Matchers>
    </ComicBookMatcher>
    <ComicBookMatcher xsi:type="ComicBookGroupMatcher" MatcherMode="Or">
      <Matchers>
        <ComicBookMatcher xsi:type="ComicBookFileMatcher" MatchOperator="1">
          <MatchValue>TPB</MatchValue>
        </ComicBookMatcher>
        <ComicBookMatcher xsi:type="ComicBookFileMatcher" MatchOperator="3">
          <MatchValue>Trade Paper Back</MatchValue>
        </ComicBookMatcher>
      </Matchers>
    </ComicBookMatcher>
  </Matchers>
</ReadingList>