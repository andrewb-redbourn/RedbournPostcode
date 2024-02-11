--- This is the create script for the postcode table within the sqlite database postcodelatlong.sqlite

create table main.postcodell
(
    postcode TEXT collate NOCASE not null
        constraint postcodell_pk
            primary key,
    lat      real                not null,
    lon      real                not null
);

create unique index postcodell_postcode_uindex
    on main.postcodell (postcode);
