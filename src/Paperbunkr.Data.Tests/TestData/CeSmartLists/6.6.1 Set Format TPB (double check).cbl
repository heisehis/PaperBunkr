<?xml version="1.0"?>
<ReadingList xmlns:xsd="http://www.w3.org/2001/XMLSchema" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" MatcherMode="Or">
  <Name>6.6.1 Set Format TPB (double check)</Name>
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
          <MatchValue>Crossover</MatchValue>
        </ComicBookMatcher>
        <ComicBookMatcher xsi:type="ComicBookFormatMatcher" Not="true">
          <MatchValue>Event</MatchValue>
        </ComicBookMatcher>
        <ComicBookMatcher xsi:type="ComicBookFormatMatcher" Not="true">
          <MatchValue>Director's Cut</MatchValue>
        </ComicBookMatcher>
        <ComicBookMatcher xsi:type="ComicBookFormatMatcher" Not="true">
          <MatchValue>Annual</MatchValue>
        </ComicBookMatcher>
        <ComicBookMatcher xsi:type="ComicBookFormatMatcher" Not="true">
          <MatchValue>TPB</MatchValue>
        </ComicBookMatcher>
        <ComicBookMatcher xsi:type="ComicBookNumberMatcher">
          <MatchValue>1</MatchValue>
        </ComicBookMatcher>
        <ComicBookMatcher xsi:type="ComicBookCustomValuesMatcher" Not="true">
          <MatchValue>comicvine_issue</MatchValue>
        </ComicBookMatcher>
        <ComicBookMatcher xsi:type="ComicBookPageCountMatcher" MatchOperator="1">
          <MatchValue>100</MatchValue>
        </ComicBookMatcher>
      </Matchers>
    </ComicBookMatcher>
  </Matchers>
</ReadingList>