<?xml version="1.0"?>
<ReadingList xmlns:xsd="http://www.w3.org/2001/XMLSchema" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
  <Name>6.3.1 Rules: TPB</Name>
  <Books />
  <Matchers>
    <ComicBookMatcher xsi:type="ComicBookGroupMatcher" MatcherMode="Or">
      <Matchers>
        <ComicBookMatcher xsi:type="ComicBookFormatMatcher" MatchOperator="1">
          <MatchValue>TPB</MatchValue>
        </ComicBookMatcher>
        <ComicBookMatcher xsi:type="ComicBookFormatMatcher" MatchOperator="1">
          <MatchValue>hard cover</MatchValue>
        </ComicBookMatcher>
        <ComicBookMatcher xsi:type="ComicBookFormatMatcher" MatchOperator="1">
          <MatchValue>trade paper back</MatchValue>
        </ComicBookMatcher>
        <ComicBookMatcher xsi:type="ComicBookSeriesMatcher" MatchOperator="1">
          <MatchValue>TPB</MatchValue>
        </ComicBookMatcher>
        <ComicBookMatcher xsi:type="ComicBookSeriesMatcher" MatchOperator="1">
          <MatchValue>trade paper back</MatchValue>
        </ComicBookMatcher>
        <ComicBookMatcher xsi:type="ComicBookSeriesMatcher" MatchOperator="1">
          <MatchValue>hard cover</MatchValue>
        </ComicBookMatcher>
        <ComicBookMatcher xsi:type="ComicBookSeriesMatcher" MatchOperator="1">
          <MatchValue>Graphic Novel</MatchValue>
        </ComicBookMatcher>
        <ComicBookMatcher xsi:type="ComicBookTitleMatcher" MatchOperator="1">
          <MatchValue>TPB</MatchValue>
        </ComicBookMatcher>
        <ComicBookMatcher xsi:type="ComicBookTitleMatcher" MatchOperator="1">
          <MatchValue>trade paper back</MatchValue>
        </ComicBookMatcher>
        <ComicBookMatcher xsi:type="ComicBookTitleMatcher" MatchOperator="1">
          <MatchValue>hard cover</MatchValue>
        </ComicBookMatcher>
        <ComicBookMatcher xsi:type="ComicBookTitleMatcher">
          <MatchValue>GN</MatchValue>
        </ComicBookMatcher>
        <ComicBookMatcher xsi:type="ComicBookSeriesMatcher" MatchOperator="1">
          <MatchValue>graphic novel</MatchValue>
        </ComicBookMatcher>
        <ComicBookMatcher xsi:type="ComicBookTitleMatcher" MatchOperator="1">
          <MatchValue>graphic novel</MatchValue>
        </ComicBookMatcher>
        <ComicBookMatcher xsi:type="ComicBookTitleMatcher">
          <MatchValue>OGN</MatchValue>
        </ComicBookMatcher>
        <ComicBookMatcher xsi:type="ComicBookTitleMatcher">
          <MatchValue>GN</MatchValue>
        </ComicBookMatcher>
        <ComicBookMatcher xsi:type="ComicBookTitleMatcher">
          <MatchValue>HC</MatchValue>
        </ComicBookMatcher>
        <ComicBookMatcher xsi:type="ComicBookTitleMatcher">
          <MatchValue>SC</MatchValue>
        </ComicBookMatcher>
        <ComicBookMatcher xsi:type="ComicBookTitleMatcher">
          <MatchValue>HC/TPB</MatchValue>
        </ComicBookMatcher>
        <ComicBookMatcher xsi:type="ComicBookTitleMatcher">
          <MatchValue>volume</MatchValue>
        </ComicBookMatcher>
        <ComicBookMatcher xsi:type="ComicBookTitleMatcher">
          <MatchValue>TPB/HC</MatchValue>
        </ComicBookMatcher>
        <ComicBookMatcher xsi:type="ComicBookTitleMatcher">
          <MatchValue>HC/SC</MatchValue>
        </ComicBookMatcher>
        <ComicBookMatcher xsi:type="ComicBookTitleMatcher">
          <MatchValue>SC/HC</MatchValue>
        </ComicBookMatcher>
        <ComicBookMatcher xsi:type="ComicBookTitleMatcher">
          <MatchValue>HC\TPB</MatchValue>
        </ComicBookMatcher>
        <ComicBookMatcher xsi:type="ComicBookTitleMatcher">
          <MatchValue>Premiere HC</MatchValue>
        </ComicBookMatcher>
        <ComicBookMatcher xsi:type="ComicBookFileMatcher">
          <MatchValue>TPB</MatchValue>
        </ComicBookMatcher>
        <ComicBookMatcher xsi:type="ComicBookSummaryMatcher" MatchOperator="7">
          <MatchValue>#[0-9]-[0-9]</MatchValue>
        </ComicBookMatcher>
      </Matchers>
    </ComicBookMatcher>
    <ComicBookMatcher xsi:type="ComicBookGroupMatcher">
      <Matchers>
        <ComicBookMatcher xsi:type="ComicBookFormatMatcher" Not="true">
          <MatchValue>Black &amp; White</MatchValue>
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
        <ComicBookMatcher xsi:type="ComicBookCountMatcher">
          <MatchValue>1</MatchValue>
          <MatchValue2>1</MatchValue2>
        </ComicBookMatcher>
        <ComicBookMatcher xsi:type="ComicBookCustomValuesMatcher">
          <MatchValue>datamanager_processed</MatchValue>
          <MatchValue2>yes</MatchValue2>
        </ComicBookMatcher>
      </Matchers>
    </ComicBookMatcher>
  </Matchers>
</ReadingList>