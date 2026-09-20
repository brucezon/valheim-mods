#!/usr/bin/perl
# Mod scout: list Thunderstore Valheim packages updated since a date, as TSV.
#   perl tools/mod-scout.pl 2026-09-09 [path/to/ts.json] > scout.tsv
# Without a json path it downloads the index (~170 MB) to $TEMP. Needs curl + perl (Git Bash).
# Columns: date_updated rating downloads deprecated owner name version created categories description
# Then e.g.:  awk -F'\t' '$8>="2026-09-09"' scout.tsv | sort -t$'\t' -k3,3nr | head   (new since date, by downloads)
use strict; use warnings;
my $since = shift or die "usage: mod-scout.pl <YYYY-MM-DD> [ts.json]\n";
my $json = shift;
if (!$json) {
  my $tmp = $ENV{TEMP} || $ENV{TMP} || '/tmp';
  $json = "$tmp/thunderstore-valheim.json";
  system("curl -sL -o \"$json\" https://thunderstore.io/c/valheim/api/v1/package/") == 0 or die "download failed\n";
}
local $/; open my $fh,'<',$json or die "$json: $!"; my $s=<$fh>; close $fh;
my @pk = split /(?=\{"name":"[^"]*","full_name":"[^"]*","owner":")/, $s;
shift @pk;
my ($n,$m)=(0,0);
print join("\t", qw(date_updated rating downloads deprecated owner name version created categories description)),"\n";
for my $p (@pk){
  my ($name)=$p=~/^\{"name":"([^"]*)"/; my ($owner)=$p=~/"owner":"([^"]*)"/;
  my ($cre)=$p=~/"date_created":"([^"]*)"/; my ($upd)=$p=~/"date_updated":"([^"]*)"/;
  my ($rat)=$p=~/"rating_score":(\d+)/; my ($dep)=$p=~/"is_deprecated":(true|false)/;
  my ($cats)=$p=~/"categories":\[([^\]]*)\]/; $cats=~s/"//g if defined $cats;
  my ($ver)=$p=~/"version_number":"([^"]*)"/;
  my ($desc)=$p=~/"description":"(.*?)","icon"/s;
  my $dl=0; $dl+=$1 while $p=~/"downloads":(\d+)/g;
  next unless defined $upd;
  $n++;
  next if $upd lt $since;
  $m++;
  $desc//=''; $desc=~s/[\t\r\n]/ /g;
  print join("\t",$upd,$rat//0,$dl,$dep//'',$owner//'',$name//'',$ver//'',substr($cre//'',0,10),$cats//'',$desc),"\n";
}
warn "packages: $n total, $m updated since $since\n";
