<?xml version="1.0"?>
<ReadingList xmlns:xsd="http://www.w3.org/2001/XMLSchema" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" MatcherMode="Or">
  <Name>6.8 Set Format Limited Series</Name>
  <Books />
  <Matchers>
    <ComicBookMatcher xsi:type="ComicBookGroupMatcher">
      <Matchers>
        <ComicBookMatcher xsi:type="ComicBookFormatMatcher" Not="true">
          <MatchValue>Black &amp; White</MatchValue>
        </ComicBookMatcher>
        <ComicBookMatcher xsi:type="ComicBookFormatMatcher" Not="true">
          <MatchValue>Giant Size</MatchValue>
        </ComicBookMatcher>
        <ComicBookMatcher xsi:type="ComicBookFormatMatcher" Not="true">
          <MatchValue>Director's Cut</MatchValue>
        </ComicBookMatcher>
        <ComicBookMatcher xsi:type="ComicBookFormatMatcher" Not="true">
          <MatchValue>Annual</MatchValue>
        </ComicBookMatcher>
        <ComicBookMatcher xsi:type="ComicBookFormatMatcher" Not="true">
          <MatchValue>Limited Series</MatchValue>
        </ComicBookMatcher>
        <ComicBookMatcher xsi:type="ComicBookFormatMatcher" Not="true">
          <MatchValue>Preview</MatchValue>
        </ComicBookMatcher>
        <ComicBookMatcher xsi:type="ComicBookSeriesMatcher" Not="true" MatchOperator="1">
          <MatchValue>AAFES</MatchValue>
        </ComicBookMatcher>
        <ComicBookMatcher xsi:type="ComicBookCustomValuesMatcher" Not="true">
          <MatchValue>comicvine_issue</MatchValue>
        </ComicBookMatcher>
        <ComicBookMatcher xsi:type="ComicBookGroupMatcher" MatcherMode="Or">
          <Matchers>
            <ComicBookMatcher xsi:type="SmartListSeriesMaxNumbertMatcher" MatchOperator="3">
              <MatchValue>2</MatchValue>
              <MatchValue2>25</MatchValue2>
            </ComicBookMatcher>
          </Matchers>
        </ComicBookMatcher>
      </Matchers>
    </ComicBookMatcher>
  </Matchers>
</ReadingList>